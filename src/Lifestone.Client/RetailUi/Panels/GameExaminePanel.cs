using System.Globalization;
using System.Runtime.CompilerServices;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>What the examine panel is showing (retail's <c>gmExaminationUI::SetActiveExamineUI</c>, 11.6096 0x004AB7A0, shows one of three sub-windows).</summary>
public enum ExamineKind
{
    /// <summary>Nothing is examined.</summary>
    None,

    /// <summary>An item (<c>ItemExamineUI</c>, 0x004ABB70): the icon, the name and a text of lines.</summary>
    Item,

    /// <summary>A creature, an NPC or a player (<c>CreatureExamineUI</c>/<c>BasicCreatureExamineUI</c>, 0x004AD0B0): the level and the vitals.</summary>
    Creature,
}

/// <summary>
/// The examine panel (M4 3.2 s4c; <c>gmExaminationUI</c>, class 0x1000001C of layout 0x2100001C, which the gameplay layout merges into the floaty <c>gmFloatyExaminationUI</c> 0x100005F7, 310 by 400 at 180,128). The panel is re-expressed from
/// the layout and the retail functions the symbol table names: <c>gmExaminationUI::RecvNotice_ExamineObject</c> (11.6096 0x004ABB30) receives the object to examine, <c>SetTitleText</c> (0x004ADCB0) writes the name, <c>SetAppraiseInfo</c>
/// (0x004ADE60) hands the appraisal to the active sub-window, <c>ItemExamineUI::SetAppraiseInfo</c> (0x004B7ED0) fills the item window line by line (<c>Appraisal_ShowValueInfo</c> 0x004AE220, <c>ShowBurdenInfo</c> 0x004AE500,
/// <c>ShowTinkeringInfo</c> 0x004B1510, <c>ShowWeaponAndArmorData</c> 0x004B1780, <c>ShowArmorMods</c> 0x004B2600, <c>ShowUsage</c> 0x004AFFD0, <c>ShowWieldRequirements</c> 0x004B0040, <c>ShowBoostValue</c> 0x004B2B20, <c>ShowHealKitValues</c>
/// 0x004B2C40, <c>ShowRemainingUses</c> 0x004B40C0, <c>ShowDescription</c> 0x004B75B0), and <c>BasicCreatureExamineUI::SetAppraiseInfo</c> (0x004B4610) reads the level (property 25).
/// </summary>
/// <remarks>
/// <para>
/// <b>Flow.</b> <see cref="Open"/> (called by the item list's right click, <c>ClientUISystem::ExamineObject</c>, and by the paperdoll figure's body-part click, <c>gmPaperDollUI::ListenToElementMessage</c> 0x004A5FA0 and
/// <c>GetPaperDollItemUnderMouse</c> 11.6096 0x004A4C90) shows the window with the object's name, sends <see cref="IActionApi.Identify"/> (<see cref="ActionSource.Ui"/>) and fills the window when <see cref="AppraisalReceived"/> for that guid
/// comes (the model holds the answer: <see cref="IGameSessionView.TryGetAppraisal"/>). Success false is a result: the tables are shown and the profiles are not. The close button closes. The window listens to the session only while it
/// is open.
/// </para>
/// <para>
/// <b>Not built, named.</b> Each part retail shows that the model or the layout cannot yet draw answers with a named <c>TODO</c> in the log and a line saying so where the part would be: the creature's 3D view
/// (<c>TODO(m4-examine-creature-view)</c>), and the poll that re-asks while the window stays open
/// outside peace (<c>gmExaminationUI::UseTime</c> 0x004AB8B0, creatures only, <c>TODO(m3-examine-poll-mode)</c>).
/// </para>
/// <para>
/// <b>Built in M5 7 D137.</b> The inscription box (<c>GameExaminePanel.Inscription.cs</c>), every wield requirement kind with the dats' heritage and creature-type names (<see cref="WieldLine"/>), the set and the gear ratings
/// (<see cref="ExamineRatingLines"/>, lane L2; the set's pieces and spells behind <c>--examine-set-details on</c>), and the failed roll's texts (the creature's vitals in retail's value format, an item's unknown uses).
/// </para>
/// <para>
/// <b>Magic and words (M5 7 S4, D122, D125).</b> The workmanship and weapon speed carry retail's words (<see cref="WorkmanshipWord"/>, <see cref="SpeedWord"/>: the client's own literals, read from the 11.6096 executable; closes
/// <c>TODO(m4-examine-words)</c>). A magic item shows its spells by name in retail's place ("Spells: ..."), what activates them in retail's words ("Activation requires Arcane Lore: N, ..."; closes
/// <c>TODO(m5-examine-activation-wording)</c> and <c>TODO(m5-examine-skill-limit)</c>), a caster's bonuses in retail's three lines (closes <c>TODO(m5-examine-caster-wording)</c>), and near the end the spellcraft, the mana now and at
/// most ("Mana: N / M."), the mana it burns, each spell's description and the enchantments on the item (closes <c>TODO(m4-examine-enchantments)</c>). The lines are one a row; retail's blank line before a double-spaced line
/// (<c>ItemExamineUI::AddItemInfo</c> 0x004AE020's last argument) is not drawn, except where a retail line holds one itself. The floaty's half of the window is <see cref="UiFloatyExamination"/>.
/// </para>
/// <para>
/// <b>Spell-changed values (M5 7 D142).</b> A line whose value an enchantment raised or lowered is drawn whole in the raised or lowered colour, as retail's <c>AddItemInfo</c> draws it (<see cref="ItemPage"/>,
/// <c>GameExaminePanel.Enchanted.cs</c>): the damage and speed, the defence bonus, the armour level and the protections (now shown, <c>Appraisal_ShowArmorMods</c>), the mana conversion and elemental bonuses.
/// </para>
/// </remarks>
public sealed partial class GameExaminePanel : IUiElementListener, IDisposable
{
    /// <summary>The floaty that holds the window in the gameplay layout (<c>gmFloatyExaminationUI</c>, class 0x1000004C).</summary>
    public const uint WindowId = 0x100005F7;

    /// <summary>The floaty's close button.</summary>
    public const uint CloseButtonId = 0x100005F3;

    /// <summary>The window's title text (retail's default text is "Examination"; <c>SetTitleText</c> writes the name).</summary>
    public const uint TitleId = 0x1000012D;

    /// <summary>The item window's field (<c>ItemExamineUI::m_itemExamineBaseField</c>).</summary>
    public const uint ItemFieldId = 0x1000012E;

    /// <summary>The creature window's field (<c>BasicCreatureExamineUI::m_basicCreatureExamineBaseField</c>).</summary>
    public const uint CreatureFieldId = 0x10000140;

    /// <summary>The spell window's field (not built: spells are another milestone's).</summary>
    public const uint SpellFieldId = 0x10000153;

    /// <summary>The item window's name text (<c>ItemExamineUI</c> constructor 0x004ABB70).</summary>
    public const uint ItemNameId = 0x10000138;

    /// <summary>The item window's second title line.</summary>
    public const uint ItemSubNameId = 0x10000139;

    /// <summary>
    /// The item window's icon: the standalone layout 0x2100001C has it (244,12, 32 by 32), but the floaty's merged layout the gameplay layout uses has none (retail's <c>m_itemIcon</c> is then null: <c>ItemExamineUI::SetAppraiseInfo</c> 0x004B7ED0
    /// skips it). Lifestone makes that element from the standalone layout as a child of the item field (M4 4.1 v4; closes <c>TODO(m4-examine-icon)</c>, a deliberate addition: the owner wants the item's art on the panel; per the fidelity rule it is behind <c>--examine-icon on</c> (<see cref="UiServices.ExamineIcon"/>, default off = retail's window), review R8) and draws the object's composed icon
    /// in it (<c>IconData::RenderIcons</c> 0x0058DFB0: the type background, the underlay, the icon and its overlay, the glow), the same picture as the pack's cell; <see cref="IconId"/> names the icon.
    /// </summary>
    public const uint ItemIconId = 0x1000013B;

    /// <summary>The standalone examine layout (class 0x1000001C), which holds the icon element the floaty's merged layout lacks.</summary>
    public const uint StandaloneLayoutId = 0x2100001C;

    /// <summary>The item window's text of lines (<c>m_itemDisplayText</c>, with its scroll bar 0x1000013D).</summary>
    public const uint ItemTextId = 0x1000013C;

    /// <summary>The item window's inscription text.</summary>
    public const uint InscriptionId = 0x1000013E;

    /// <summary>The creature window's level value (<c>m_levelValueText</c>).</summary>
    public const uint LevelTextId = 0x1000014C;

    /// <summary>ItemType bit of a creature (<c>ITEM_TYPE::TYPE_CREATURE</c>).</summary>
    public const uint CreatureItemType = 0x10;

    private static readonly ConditionalWeakTable<UiLoginContext, GameExaminePanel> ByContext = new();

    private readonly UiLoginContext _context;
    private readonly UiElement _window;
    private readonly UiElement? _itemField;
    private readonly UiElement? _creatureField;
    private readonly UiElement? _spellField;
    private readonly UiTextElement? _title;
    private readonly UiTextElement? _itemName;
    private readonly UiTextElement? _itemSubName;
    private readonly UiElement? _itemIcon;
    private readonly IconRetry _retry;
    private bool _retryRegistered;
    private readonly UiTextElement? _itemText;
    private readonly UiTextElement? _level;
    private IDisposable? _subscription;
    private bool _disposed;

    private GameExaminePanel(UiLoginContext context, UiElement window)
    {
        _context = context;
        _window = window;
        _itemField = window.GetChildRecursive(ItemFieldId);
        _creatureField = window.GetChildRecursive(CreatureFieldId);
        _spellField = window.GetChildRecursive(SpellFieldId);
        _title = window.GetChildRecursive(TitleId) as UiTextElement;
        _itemName = window.GetChildRecursive(ItemNameId) as UiTextElement;
        _itemSubName = window.GetChildRecursive(ItemSubNameId) as UiTextElement;
        _retry = new IconRetry(this);
        _itemIcon = window.GetChildRecursive(ItemIconId) ?? (context.Manager.Services.ExamineIcon ? MakeIconElement(_itemField) : null); // the icon is an addition to retail: only with --examine-icon on
        _itemText = window.GetChildRecursive(ItemTextId) as UiTextElement;
        _level = window.GetChildRecursive(LevelTextId) as UiTextElement;
        window.RegisterForElementMessages(this);
        _window.SetVisible(false); // retail shows the floaty when something is examined
        _spellField?.SetVisible(false);
        _itemField?.SetVisible(false);
        _creatureField?.SetVisible(false);
    }

    /// <summary>
    /// The panel of the shell that holds <paramref name="anyElement"/> (any element of the gameplay layout), made on first use; null when the layout has no examine floaty. Registered for <paramref name="context"/> so the item lists and the
    /// paperdoll find it with <see cref="Of"/>.
    /// </summary>
    public static GameExaminePanel? Attach(UiLoginContext context, UiElement anyElement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(anyElement);
        var root = anyElement;
        while (root.Parent is { } parent)
        {
            root = parent;
        }

        if (root.GetChildRecursive(WindowId) is not { } window)
        {
            context.Manager.Services.Log.Warn("retailui", "gmFloatyExaminationUI: the gameplay layout has no examine window 0x100005F7; nothing can be examined (TODO(m4-examine-window-missing))");
            return null;
        }

        if (ByContext.TryGetValue(context, out var existing) && ReferenceEquals(existing._window, window))
        {
            return existing;
        }

        var panel = new GameExaminePanel(context, window);
        ByContext.AddOrUpdate(context, panel);
        var target = context.Manager.TargetMode;
        target.Examiner = guid => panel.Open(guid); // ClientUISystem::ExamineObject: a list with no owner of its own, the toolbar's Examine and the examine target mode reach the window here (M5 7 S6)
        return panel;
    }

    /// <summary>The panel of the shell logged in with <paramref name="context"/>, or null before <see cref="Attach"/> ran (or when the layout has none).</summary>
    public static GameExaminePanel? Of(UiLoginContext context) => ByContext.TryGetValue(context, out var panel) ? panel : null;

    /// <summary>True while the window shows.</summary>
    public bool IsOpen => _window.Visible;

    /// <summary>The window's element (the floaty).</summary>
    public UiElement Window => _window;

    /// <summary>The object examined now (0 when nothing is).</summary>
    public uint ShownGuid { get; private set; }

    /// <summary>The kind of window that shows.</summary>
    public ExamineKind Kind { get; private set; }

    /// <summary>True from <see cref="Open"/> until the appraisal arrives: the window shows the name and "???" lines.</summary>
    public bool Pending { get; private set; }

    /// <summary>The title now (the object's name).</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The item window's lines now, one per row.</summary>
    public IReadOnlyList<string> Lines { get; private set; } = [];

    /// <summary>The font colour index of each of <see cref="Lines"/> (<see cref="ExamineColor"/>; M5 7 D142: a line an enchantment raised or lowered).</summary>
    public IReadOnlyList<int> LineColors { get; private set; } = [];

    /// <summary>The icon the item window shows (0 for none).</summary>
    public uint IconId { get; private set; }

    /// <summary>The fields the icon was composed from (the object's icon, underlay, overlay, item type and effects).</summary>
    public UiItemIcon Icon { get; private set; }

    /// <summary>The element the icon is drawn in (made from the standalone layout), or null when the layout could not give it.</summary>
    public UiElement? IconElement => _itemIcon;

    /// <summary>True while an image of the icon is still decoding (the composition is retried each frame).</summary>
    public bool IconPending { get; private set; }

    /// <summary>How many times the icon was composed.</summary>
    public int IconCompositions { get; private set; }

    /// <summary>The last Identify <see cref="Open"/> sent, or null.</summary>
    public IdentifyHandle? LastIdentify { get; private set; }

    /// <summary>Whether the answer the window shows was a successful roll, or null before it came (M5 7 D138: the capture's frames tell a full page from a failed roll).</summary>
    public bool? ShownSuccess { get; private set; }

    /// <summary>How many times <see cref="Open"/> sent an Identify.</summary>
    public int Opens { get; private set; }

    /// <summary>How many appraisals the window filled from.</summary>
    public int Fills { get; private set; }

    /// <summary>
    /// <c>gmExaminationUI::RecvNotice_ExamineObject</c> (11.6096 0x004ABB30): shows the window for <paramref name="guid"/> and asks the server about it. False for guid 0. The window shows the object's name at once; the answer fills it.
    /// </summary>
    public bool Open(uint guid)
    {
        if (guid == 0 || _disposed)
        {
            return false;
        }

        var session = _context.Session;
        WorldObject? obj = session.State.Objects.TryGet(guid, out var found) ? found : null;
        var stock = obj is null ? session.State.Vendor?.Find(guid) : null; // a vendor's row is no world object: its name and icon come from the vendor's list (M5 7 S6)
        ShownGuid = guid;
        Pending = true;
        Kind = obj is not null && (obj.ItemType & CreatureItemType) != 0 ? ExamineKind.Creature : ExamineKind.Item;
        SetTitle(obj?.Name is { Length: > 0 } name ? name : stock?.Name is { Length: > 0 } stockName ? stockName : "???");
        var objectIcon = obj is not null ? GameInventoryPage.IconOf(obj) : stock is not null ? GameVendorPanel.IconOf(stock, 0) : default;
        IconId = objectIcon.IconId;
        ShowKind(Kind);
        if (Kind == ExamineKind.Item)
        {
            SetItem(objectIcon, new ExamineItemPage(["Value: ???", "Burden: ???"], [ExamineColor.Normal, ExamineColor.Normal]));
        }
        else
        {
            ShowPendingCreature(); // M5 7 D137: the creature window waits for the answer
        }

        ClearInscription(); // the box waits for the answer (SetInscription runs with the appraisal)
        ShownSuccess = null;
        _window.SetVisible(true);
        _subscription ??= session.Events.Subscribe<AppraisalReceived>(OnAppraisal);
        LastIdentify = session.Actions.Identify(guid, ActionSource.Ui);
        Opens++;
        return true;
    }

    /// <summary>
    /// The lines the examination's review frame needs on screen (M5 7 S6, D136): the spells by name ("Spells: "), the activation line ("Activation requires "), the mana ("Mana: ") and the workmanship ("Workmanship: "). Null when
    /// <paramref name="lines"/> shows all four, else the missing ones, comma separated.
    /// </summary>
    public static string? MissingReviewLines(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<string>? missing = null;
        foreach (var (prefix, name) in ReviewLines)
        {
            if (!lines.Any(l => l.StartsWith(prefix, StringComparison.Ordinal) && !l.StartsWith("Spells: unknown", StringComparison.Ordinal)))
            {
                (missing ??= []).Add(name);
            }
        }

        return missing is null ? null : string.Join(",", missing);
    }

    private static readonly (string Prefix, string Name)[] ReviewLines = [("Spells: ", "spells"), ("Activation requires ", "activation"), ("Mana: ", "mana"), ("Workmanship: ", "workmanship")];

    /// <summary>Hides the window and stops listening to the session.</summary>
    public void Close()
    {
        _window.SetVisible(false);
        _subscription?.Dispose();
        _subscription = null;
        StopRetry();
        ShownGuid = 0;
        Kind = ExamineKind.None;
        Pending = false;
    }

    /// <summary>The floaty's close button.</summary>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ButtonClick && message.ElementId == CloseButtonId)
        {
            Close();
            return UiListenResult.Handled;
        }

        InscriptionMessage(message); // the inscription's press and focus (M5 7 D137)
        return UiListenResult.Default;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Close();
        _window.UnregisterForElementMessages(this);
        if (ByContext.TryGetValue(_context, out var current) && ReferenceEquals(current, this))
        {
            ByContext.Remove(_context);
            _context.Manager.TargetMode.Examiner = null;
            _context.Manager.TargetMode.Set(UiTargetModeKind.None);
        }
    }

    private void OnAppraisal(in AppraisalReceived received)
    {
        if (received.Guid != ShownGuid || ShownGuid == 0 || !_context.Session.TryGetAppraisal(ShownGuid, out var appraisal))
        {
            return;
        }

        Fill(appraisal);
    }

    /// <summary><c>gmExaminationUI::SetAppraiseInfo</c> (0x004ADE60): the answer goes to the active window.</summary>
    private void Fill(Appraisal appraisal)
    {
        LogAnswer(appraisal);
        ShownSuccess = appraisal.Success;
        var session = _context.Session;
        WorldObject? obj = session.State.Objects.TryGet(ShownGuid, out var found) ? found : null;
        var stock = obj is null ? session.State.Vendor?.Find(ShownGuid) : null;
        Pending = false;
        Fills++;
        var name = appraisal.Name is { Length: > 0 } n ? n : obj?.Name is { Length: > 0 } on ? on : stock?.Name is { Length: > 0 } sn ? sn : "???";
        SetTitle(name);
        var log = _context.Manager.Services.Log;
        var page = ExamineCreatureLines.Build(appraisal, ContextFor(appraisal, obj), log); // gmExaminationUI::SetAppraiseInfo 0x004ADE60: a creature profile picks the creature window (M5 7 D137, lane L3)
        var creature = page is not null || (appraisal.ItemType is { } type ? (type & CreatureItemType) != 0 : obj is not null && (obj.ItemType & CreatureItemType) != 0);
        Kind = creature ? ExamineKind.Creature : ExamineKind.Item;
        ShowKind(Kind);
        if (page is not null)
        {
            ShowCreaturePage(page);
        }
        else if (creature)
        {
            ShowPendingCreature(); // no creature profile (ACE always sends one for a creature): the values stay unknown
            LevelText = ExamineCreatureLines.LevelText(appraisal);
            _level?.SetText(LevelText);
        }
        else
        {
            // The object's composed icon; the Appraisal's own icon (property data 8) only when the world table has none for it.
            var icon = obj is not null && obj.ItemFields().IconId != 0 ? GameInventoryPage.IconOf(obj) : stock is not null && stock.IconId != 0 ? GameVendorPanel.IconOf(stock, 0) : new UiItemIcon(appraisal.DataId(8) ?? 0, 0, 0, obj?.ItemType ?? 0, 0);
            var enumNames = _context.Manager.Services.EnumNames;
            SetItem(icon, ItemPage(appraisal, log, session.Spell, enumNames is null ? null : enumNames.GetString, obj?.DescriptionFlags ?? 0, SetDetails(appraisal)));
            LogEnchantedLines(appraisal); // M5 7 D142
            SetInscription(appraisal, obj); // ItemExamineUI::SetInscription 0x004AE670 (M5 7 D137)
        }

        LogShownLines();
    }

    /// <summary>
    /// The set details (<c>--examine-set-details on</c>, an addition to retail; M5 7 D137): for an item of a set retail names, the pieces of that set the player wears (each worn item's set from its own appraisal; one not yet appraised
    /// counts as unknown) and the set's spells by pieces (<see cref="ExamineSetLines.WornLines"/>). Null when the option is off or the item has no named set.
    /// </summary>
    private IReadOnlyList<string>? SetDetails(Appraisal appraisal)
    {
        if (!_context.Manager.Services.ExamineSetDetails || ExamineSetLines.SetIdOf(appraisal) is not { } setId || ExamineSetLines.SetName(setId) is null)
        {
            return null;
        }

        var session = _context.Session;
        var worn = new List<int?>();
        foreach (var obj in session.State.Objects.All)
        {
            if (obj.WielderId == session.State.Self.Guid)
            {
                worn.Add(session.TryGetAppraisal(obj.Guid, out var wornAppraisal) ? wornAppraisal.Int(ExamineSetLines.EquipmentSetId) ?? 0 : null);
            }
        }

        var (pieces, unknown) = ExamineSetLines.CountWorn(setId, worn);
        return ExamineSetLines.WornLines(appraisal, pieces, session.SpellSets ?? new Dictionary<uint, IReadOnlyDictionary<uint, IReadOnlyList<uint>>>(), session.Spell, _context.Manager.Services.Log, unknown);
    }

    // --- the lines (static: a test reads them without a shell) ----------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>ItemExamineUI::SetAppraiseInfo</c> (11.6096 0x004B7ED0): the item window's lines in retail's order: value, burden, the tinkering and workmanship, the ratings and set, the weapon's and armour's data, the armour level, the
    /// item's spells by name, the usage, the wield requirements, what activates the spells, a caster's bonuses, the potion's or kit's boost, a mana stone's charge, the uses left, the sale rule, the spellcraft, mana and spell descriptions,
    /// and the description.
    /// </summary>
    /// <param name="a">The appraisal.</param>
    /// <param name="log">Where a named TODO goes.</param>
    /// <param name="spell">The SpellTable lookup (<see cref="IGameSessionView.Spell"/>) that names the item's spells; null names none (each shows as "Spell N" with a logged TODO).</param>
    /// <param name="enumString">The dat's EnumMapper strings (<see cref="Lifestone.Data.Dat.EnumMapperNames.GetString"/>: map id, value): the heritage and creature-type names of a requirement; null names none (the line is left out, a named TODO).</param>
    /// <param name="objectFlags">The object's ObjectDescriptionFlags (its healer 0x10000 and lockpick 0x20000 bits make a failed appraisal say its uses are unknown); 0 when the world table has no object.</param>
    /// <param name="setDetails">Lines under "Set: name" (<c>--examine-set-details on</c>: the pieces worn and the set's spells, <see cref="ExamineSetLines.WornLines"/>), or null for retail's name alone.</param>
    public static IReadOnlyList<string> ItemLines(Appraisal a, ILogSink log, Func<uint, Lifestone.Data.Magic.SpellInfo?>? spell = null, Func<uint, uint, string?>? enumString = null, uint objectFlags = 0, IReadOnlyList<string>? setDetails = null) =>
        ItemPage(a, log, spell, enumString, objectFlags, setDetails).Lines;

    /// <summary>
    /// <see cref="ItemLines"/> with each line's font colour index (M5 7 D142): the lines an enchantment raised or lowered (the appraisal's armour, weapon and resist masks, <see cref="EnchantmentColor"/>) in <see cref="ExamineColor.Raised"/> or
    /// <see cref="ExamineColor.Lowered"/>, every other line <see cref="ExamineColor.Normal"/>. The parameters are <see cref="ItemLines"/>'.
    /// </summary>
    public static ExamineItemPage ItemPage(Appraisal a, ILogSink log, Func<uint, Lifestone.Data.Magic.SpellInfo?>? spell = null, Func<uint, uint, string?>? enumString = null, uint objectFlags = 0, IReadOnlyList<string>? setDetails = null)
    {
        ArgumentNullException.ThrowIfNull(a);
        var lines = new List<string>(12);
        var colors = new Dictionary<int, int>();
        lines.Add(a.Value is { } value ? "Value: " + Number(value) : "Value: ???"); // Appraisal_ShowValueInfo 0x004AE220
        lines.Add(a.Burden is { } burden ? "Burden: " + Number(burden) : "Burden: ???"); // Appraisal_ShowBurdenInfo 0x004AE500
        TinkeringLines(a, lines); // Appraisal_ShowTinkeringInfo 0x004B1510
        lines.AddRange(ExamineRatingLines.SetAndRatings(a, setDetails)); // Appraisal_ShowSet 0x004AEC00, Appraisal_ShowRatings 0x004AF400 and SetAppraiseInfo's blank line (M5 7 D137, lane L2)

        if (a.Weapon is not null || a.WeaponDamage is not null)
        {
            WeaponLines(a, lines, colors);
        }

        DefenseModLines(a, lines, colors); // Appraisal_ShowDefenseModData 0x004B2490 (M5 7 D142)
        ArmorModLines(a, lines, colors); // Appraisal_ShowArmorMods 0x004B2600 (M5 7 D142: the protections and the colours)

        SpellNamesLine(a, lines, log, spell); // Appraisal_ShowShortMagicInfo 0x004B3330
        if (a.UseText is { Length: > 0 } use)
        {
            lines.Add(use); // Appraisal_ShowUsage 0x004AFFD0
        }

        WieldLines(a, lines, log, enumString); // Appraisal_ShowWieldRequirements 0x004B0040
        ActivationLines(a, lines, log, enumString); // Appraisal_ShowActivationRequirements 0x004B3910
        CasterLines(a, lines, colors); // Appraisal_ShowCasterData 0x004B21B0
        if (a.HealingBoost is not null || a.Float(100) is not null)
        {
            BoostLines(a, lines);
        }

        ManaStoneLines(a, lines); // Appraisal_ShowManaStoneInfo 0x004B3FA0
        if (a.UsesLeft is { } left)
        {
            lines.Add("Number of uses remaining: " + left.ToString(CultureInfo.InvariantCulture)); // Appraisal_ShowRemainingUses 0x004B40C0
        }
        else if (!a.Success && ((objectFlags & HealerOrLockpickFlags) != 0 || (a.HookFlags is { } hook && (hook & HookHealerOrLockpickFlags) != 0)))
        {
            lines.Add("Number of uses remaining:  Unknown"); // the failed roll's line (Appraisal_ShowRemainingUses), two spaces as retail's literal (M5 7 D137)
        }

        if (a.Bool(69) == false)
        {
            lines.Add("This item cannot be sold.");
        }

        MagicLines(a, lines, log, spell); // Appraisal_ShowMagicInfo 0x004B34B0

        if (a.LongDescription is { Length: > 0 } description)
        {
            lines.Add(description); // Appraisal_ShowDescription 0x004B75B0
        }

        var lineColors = new int[lines.Count];
        foreach (var (index, color) in colors)
        {
            lineColors[index] = color;
        }

        return new ExamineItemPage(lines, lineColors);
    }

    /// <summary>
    /// One INFO line an answer the window shows (M5 7 D137; the failed-roll live capture reads it): the guid, the name, success, the flags of the answer and of its creature profile, the health it holds, and the window's lines once filled
    /// (<see cref="LogShownLines"/>).
    /// </summary>
    private void LogAnswer(Appraisal a)
    {
        var flags = a.CreatureProfileFlags;
        _context.Manager.Services.Log.Info(
            "retailui",
            string.Create(CultureInfo.InvariantCulture, $"examine answer guid=0x{a.Guid:X8} name=\"{a.Name}\" success={a.Success} flags=0x{(uint)a.Flags:X} creature_flags={(flags is null ? "none" : "0x" + flags.Value.ToString("X", CultureInfo.InvariantCulture))} health={(a.CreatureHealth is null ? "none" : a.CreatureHealth + "/" + a.CreatureHealthMax)} attributes={(a.CreatureShowsAttributes ? "yes" : "no")} ints={a.Ints.Count} strings={a.Strings.Count} spells={a.Spells.Length}"));
    }

    private void LogShownLines()
    {
        var shown = Kind == ExamineKind.Creature ? string.Join(" | ", [LevelText, .. VitalLines]) : string.Join(" | ", Lines);
        _context.Manager.Services.Log.Info("retailui", $"examine shown guid=0x{ShownGuid:X8} kind={Kind} title=\"{Title}\" lines=[{shown}]");
    }

    /// <summary>ObjectDescriptionFlags Healer (0x10000) and Lockpick (0x20000): a failed roll on such an item says its uses are unknown (<c>Appraisal_ShowRemainingUses</c>' <c>pwd._bitfield &amp; 0x30000</c>).</summary>
    internal const uint HealerOrLockpickFlags = 0x30000;

    /// <summary>The hook profile's IsHealer (0x2) and IsLockpick (0x8) flags (<c>AppraisalProfile::IsHookedItemHealer</c>, <c>IsHookedItemLockpick</c>).</summary>
    internal const uint HookHealerOrLockpickFlags = 0x2 | 0x8;

    /// <summary>PropertyFloat 152 (ACE <c>ElementalDamageMod</c>): a caster's multiplier on its spells' damage (1.03 is +3%).</summary>
    internal const uint ElementalDamageModId = 152;

    /// <summary>PropertyFloat 144 (ACE <c>ManaConversionMod</c>): a caster's bonus to Mana Conversion (0.05 is +5%).</summary>
    internal const uint ManaConversionModId = 144;

    /// <summary>PropertyFloat 5 (ACE <c>ManaRate</c>): the mana a worn item burns a second (negative).</summary>
    internal const uint ManaRateId = 5;

    /// <summary>PropertyInt 117 (ACE <c>ItemManaCost</c>): the mana an item's use costs.</summary>
    internal const uint ItemManaCostId = 117;

    /// <summary>PropertyInt 170 (ACE <c>NumItemsInMaterial</c>): how many items a salvage bag was made from.</summary>
    internal const uint SalvagedItemsId = 170;

    /// <summary>PropertyInt 265 (ACE <c>EquipmentSetId</c>): the set an item belongs to, the one property <c>Appraisal_ShowSet</c> (11.6096 0x004AEC00) asks for (before M5 7 S4 the placeholder looked for 170 instead).</summary>
    internal const uint SetId = 265;

    /// <summary>PropertyInt 171 (ACE <c>NumTimesTinkered</c>).</summary>
    internal const uint TinkeredId = 171;

    /// <summary>PropertyInt 204 (ACE <c>ElementalDamageBonus</c>): a weapon's flat elemental bonus.</summary>
    internal const uint ElementalDamageBonusId = 204;

    /// <summary>
    /// <c>CombatSystem::ElementalModPKModifier</c>, the share of a caster's elemental bonus that counts against players (<c>CombatSystem::GetElementalModPKModifier</c>, 11.6096 0x005CA000: <c>(mod - 1) x share + 1</c>); the double read from
    /// the 11.6096 executable's data at 0x00823C60.
    /// </summary>
    internal const double ElementalModPKShare = 0.5;

    /// <summary>
    /// <c>AppraisalSystem::InqWorkmanshipAdjective</c> (11.6096 0x005B5990) over its table <c>rgWorkmanshipStrings</c> (the executable's data at 0x0082260C, eleven literals, read from the 11.6096 executable): the word for a workmanship of 0 to 10
    /// (above 10 is 10); for 4 and below "crafted" follows ("cut" for a gem, which the examine window never asks for).
    /// </summary>
    internal static string WorkmanshipWord(int level)
    {
        ReadOnlySpan<string> words = ["", "Poorly ", "Well-", "Finely ", "Exquisitely ", "Magnificent", "Nearly flawless", "Flawless", "Utterly flawless", "Incomparable", "Priceless"];
        var index = (uint)level >= 11 ? 10 : level; // retail compares unsigned: a negative level is "Priceless" too
        return index <= 4 ? words[index] + "crafted" : words[index];
    }

    /// <summary><c>AppraisalSystem::WeaponTimeToString</c> (11.6096 0x005B5A20): the word for a weapon's speed (its time: lower is faster).</summary>
    internal static string SpeedWord(int time) => time switch
    {
        < 11 => "Very Fast",
        < 31 => "Fast",
        < 50 => "Average",
        < 80 => "Slow",
        _ => "Very Slow",
    };

    /// <summary><c>ModifierToString</c> (11.6096 0x004AB550): a multiplier as a whole percent with its sign ("+5%", "-10%"), rounded half up.</summary>
    internal static string ModifierText(double modifier) =>
        string.Create(CultureInfo.InvariantCulture, $"{(modifier < 1.0 ? '-' : '+')}{(ulong)((Math.Abs(1.0 - modifier) * 100.0) + 0.5)}%");

    /// <summary><c>SmallModifierToString</c> (11.6096 0x004AB5C0): a multiplier as a percent to a tenth with its sign ("+3.0%").</summary>
    internal static string SmallModifierText(double modifier) =>
        string.Create(CultureInfo.InvariantCulture, $"{(modifier < 1.0 ? '-' : '+')}{(Math.Abs(1.0 - modifier) * 100.0).ToString("F1", CultureInfo.InvariantCulture)}%");

    /// <summary>
    /// <c>Appraisal_ShowTinkeringInfo</c> (11.6096 0x004B1510): how often the item was tinkered (PropertyInt 171), who tinkered it last (PropertyString 39), who imbued it (PropertyString 40) and its workmanship (PropertyInt 105) with its word; a
    /// salvage bag (PropertyInt 170, the items it was made from) shows the average to two places and how many items made it.
    /// </summary>
    private static void TinkeringLines(Appraisal a, List<string> lines)
    {
        if (a.Int(TinkeredId) is { } tinkers)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"This item has been tinkered {tinkers} time{(tinkers > 1 ? "s" : string.Empty)}."));
        }

        if (a.String(39) is { } tinkerer)
        {
            lines.Add($"Last tinkered by {tinkerer}.");
        }

        if (a.String(40) is { } imbuer)
        {
            lines.Add($"Imbued by {imbuer}.");
        }

        if (a.Workmanship is not { } workmanship)
        {
            return;
        }

        if (a.Int(SalvagedItemsId) is { } items && items != 0)
        {
            var average = (double)workmanship / items;
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Workmanship: {WorkmanshipWord((int)(average + 0.5))} ({average:F2})"));
            lines.Add(string.Empty);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Salvaged from {items} items."));
        }
        else
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Workmanship: {WorkmanshipWord(workmanship)} ({workmanship})"));
        }
    }

    /// <summary>The name and description of one of the item's spells (<c>ClientMagicSystem::GetSpellName</c> 11.6096 0x00567EF0, <c>GetSpellDescription</c> 0x00567F60), or "Spell N" with a logged TODO when the table lacks it.</summary>
    private static (string Name, string Description) SpellText(uint id, ILogSink log, Func<uint, Lifestone.Data.Magic.SpellInfo?>? spell)
    {
        if (spell?.Invoke(id) is { Name.Length: > 0 } info)
        {
            return (info.Name, info.Description);
        }

        Todo(log, "m5-examine-spell-name", $"spell {id} is not in the SpellTable (or the table has not loaded), so the examine panel shows its number");
        return ("Spell " + id.ToString(CultureInfo.InvariantCulture), string.Empty);
    }

    /// <summary>
    /// True when the answer has a spell book (retail's <c>_spellBook</c> is set). Read from the entries, not the section flag (the retail UI does not name the wire's types): ACE writes the section only when the book has an entry
    /// (<c>AppraiseInfo</c>), so the two agree.
    /// </summary>
    private static bool HasSpellBook(Appraisal a) => !a.SpellBook.IsEmpty;

    /// <summary>
    /// <c>Appraisal_ShowShortMagicInfo</c> (11.6096 0x004B3330): the item's spells by name after "Spells: ", comma separated in the order the server sent them; the enchantments on the item (the 0x80000000 entries) are not among them, and with none
    /// left there is no line. A failed appraisal with a spell book says "Spells: unknown.".
    /// </summary>
    private static void SpellNamesLine(Appraisal a, List<string> lines, ILogSink log, Func<uint, Lifestone.Data.Magic.SpellInfo?>? spell)
    {
        if (!HasSpellBook(a))
        {
            return;
        }

        if (!a.Success)
        {
            lines.Add("Spells: unknown.");
            return;
        }

        if (a.Spells.Length == 0)
        {
            return;
        }

        var names = new List<string>(a.Spells.Length);
        foreach (var id in a.Spells)
        {
            names.Add(SpellText(id, log, spell).Name);
        }

        lines.Add("Spells: " + string.Join(", ", names));
    }

    /// <summary>
    /// <c>Appraisal_ShowMagicInfo</c> (11.6096 0x004B34B0): when the item has a spell of its own, the spellcraft ("Spellcraft: N."), the mana now and at most ("Mana: N / M."), the mana it burns (from its rate, "Mana Cost: 1 point per N seconds.",
    /// else its use cost) and "Spell Descriptions:" with a "~ name: description" line a spell; then, when enchantments are on the item, "Enchantments:", a blank line and one such line each. A failed appraisal with a spell book says
    /// "Spells: unknown." (again: the short line said it too, as retail).
    /// </summary>
    private static void MagicLines(Appraisal a, List<string> lines, ILogSink log, Func<uint, Lifestone.Data.Magic.SpellInfo?>? spell)
    {
        if (!HasSpellBook(a))
        {
            return;
        }

        if (!a.Success)
        {
            lines.Add("Spells: unknown.");
            return;
        }

        if (a.Spells.Length > 0)
        {
            if (a.Int(AppraisalIds.Spellcraft) is { } spellcraft)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"Spellcraft: {spellcraft}."));
            }

            if (a.ItemMana is { } mana && a.ItemMaxMana is { } maxMana)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"Mana: {mana} / {maxMana}."));
            }

            if (a.Float(ManaRateId) is { } rate && rate != 0.0) // a rate of 0 would divide by nothing (retail's cast of the infinity is undefined): Lifestone shows no line for it
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"Mana Cost: 1 point per {(ulong)(Math.Abs(1.0 / rate) + 0.5)} seconds."));
            }
            else if (a.Int(ItemManaCostId) is { } cost)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"Mana Cost: {cost}."));
                if (cost > 0)
                {
                    lines.Add("(Can be reduced by the Mana Conversion skill)");
                }
            }

            lines.Add("Spell Descriptions:");
            foreach (var id in a.Spells)
            {
                var (name, description) = SpellText(id, log, spell);
                lines.Add($"~ {name}: {description}");
            }
        }

        if (a.ItemEnchantments.Length > 0)
        {
            lines.Add("Enchantments:");
            lines.Add(string.Empty);
            foreach (var id in a.ItemEnchantments)
            {
                var (name, description) = SpellText(id, log, spell);
                lines.Add($"~ {name}: {description}");
            }
        }
    }

    /// <summary>
    /// <c>Appraisal_ShowActivationRequirements</c> (11.6096 0x004B3910): "Activation requires " and, comma separated, the Arcane Lore (PropertyInt 109), the allegiance rank (110), the heritage (188), a skill's level (115, the skill in 176),
    /// an attribute's (258, the attribute in 257) and a vital's (260, the vital in 259); then "This item can only be activated by" the craftsman (PropertyString 25, else "the original owner") when PropertyBool 94 is set. A heritage
    /// retail names from the EnumMapper table 0x10000002 (all but Gharu'ndim, Umbraen and Olthoi) is not read here: it is left out with a named TODO, never guessed.
    /// </summary>
    private static void ActivationLines(Appraisal a, List<string> lines, ILogSink log, Func<uint, uint, string?>? enumString)
    {
        if (!a.Success)
        {
            return;
        }

        var parts = new List<string>(2);
        if (a.Int(AppraisalIds.ArcaneLore) is { } difficulty and > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Arcane Lore: {difficulty}"));
        }

        if (a.Int(110) is { } rank and >= 1)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Allegiance Rank: {rank}"));
        }

        if (a.Int(188) is { } heritage and not 0)
        {
            if (HeritageName(heritage, enumString) is { } heritageName)
            {
                parts.Add(heritageName);
            }
            else
            {
                Todo(log, "m5-examine-heritage-name", $"the item is activated only by heritage {heritage}, which the dat's EnumMapper 0x10000002 does not name (or no dats are attached); the activation line leaves it out");
            }
        }

        if (a.Int(115) is { } skillLimit and > 0 && a.Int(176) is { } skill)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{SkillName(skill)}: {skillLimit}"));
        }

        if (a.Int(258) is { } attributeLimit and > 0 && a.Int(257) is { } attribute && AttributeName(attribute) is { } attributeName)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{attributeName}: {attributeLimit}"));
        }

        if (a.Int(260) is { } vitalLimit and > 0 && a.Int(259) is { } vital && VitalName(vital) is { } vitalName)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{vitalName}: {vitalLimit}"));
        }

        if (parts.Count > 0)
        {
            lines.Add("Activation requires " + string.Join(", ", parts));
        }

        if (a.Bool(94) == true)
        {
            lines.Add($"This item can only be activated by {a.String(25) ?? "the original owner"}.");
        }
    }

    /// <summary>
    /// <c>Appraisal_ShowCasterData</c> (11.6096 0x004B21B0): a caster's bonus to Mana Conversion (PropertyFloat 144, shown as one more than it is), then its elemental damage bonus (PropertyFloat 152, with the element in PropertyInt 45) as
    /// three lines: "Damage bonus for &lt;element&gt; spells:", the bonus against monsters and the smaller one against players (<see cref="ElementalModPKShare"/>). A bonus an enchantment raised or lowered (Hermetic Link, Spirit Drinker:
    /// the resist mask's 0x1000 and 0x2000) is drawn in the raised or lowered colour, all three damage lines alike (M5 7 D142).
    /// </summary>
    private static void CasterLines(Appraisal a, List<string> lines, Dictionary<int, int> colors)
    {
        if (a.Float(ManaConversionModId) is { } manaConversion)
        {
            AddColored(lines, colors, $"Bonus to Mana Conversion: {ModifierText(manaConversion + 1.0)}.", ResistColor(a, ResistManaConversionBit));
        }

        if (a.Float(ElementalDamageModId) is { } damageMod && a.Int(AppraisalIds.DamageType) is { } element)
        {
            var color = ResistColor(a, ResistElementalDamageBit);
            AddColored(lines, colors, $"Damage bonus for {DamageTypeName(unchecked((uint)element))} spells:", color);
            AddColored(lines, colors, $" vs. Monsters: {SmallModifierText(damageMod)}.", color);
            AddColored(lines, colors, $" vs. Players: {SmallModifierText(((damageMod - 1.0) * ElementalModPKShare) + 1.0)}.", color);
        }
    }

    /// <summary><c>Appraisal_ShowManaStoneInfo</c> (11.6096 0x004B3FA0): an object with no spell book (a mana stone) shows the mana it holds (PropertyInt 107), its efficiency (PropertyFloat 87) and its chance of destruction (PropertyFloat 137).</summary>
    private static void ManaStoneLines(Appraisal a, List<string> lines)
    {
        if (HasSpellBook(a))
        {
            return;
        }

        if (a.ItemMana is { } stored)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Stored Mana: {stored}"));
        }

        if (a.Float(87) is { } efficiency)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Efficiency: {(ulong)(efficiency * 100.0)}%"));
        }

        if (a.Float(137) is { } destruction)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Chance of Destruction: {(ulong)(destruction * 100.0)}%"));
        }
    }

    /// <summary><c>SkillSystem::InqAttributeName</c> (11.6096 0x005C97E0): the six attributes' names; null for another id.</summary>
    internal static string? AttributeName(int id) => id switch
    {
        1 => "Strength", 2 => "Endurance", 3 => "Quickness", 4 => "Coordination", 5 => "Focus", 6 => "Self",
        _ => null,
    };

    /// <summary><c>SkillSystem::InqAttribute2ndName</c> (11.6096 0x005C9880): the six vital names; null for another id.</summary>
    internal static string? VitalName(int id) => id switch
    {
        1 => "Maximum Health", 2 => "Health", 3 => "Maximum Stamina", 4 => "Stamina", 5 => "Maximum Mana", 6 => "Mana",
        _ => null,
    };

    /// <summary>
    /// <c>Appraisal_ShowWeaponAndArmorData</c> (11.6096 0x004B1780): the skill, the damage (in the raised or lowered colour when an enchantment changed the greatest damage, the weapon mask's 0x8, else the variance, 0x10), the elemental
    /// bonus and the speed (the mask's 0x4; M5 7 D142).
    /// </summary>
    private static void WeaponLines(Appraisal a, List<string> lines, Dictionary<int, int> colors)
    {
        if (a.WeaponSkill is { } skill and > 0)
        {
            lines.Add("Skill: " + SkillName(skill)); // Appraisal_ShowWeaponAndArmorData 0x004B1780
        }

        if (a.WeaponDamage is { } damage)
        {
            // Retail shows the range ("%s%.3g - %d%s": the low end to three significant digits, then the greatest damage); the low end is the greatest damage less the weapon profile's variance share. Without a profile (or a variance of 0) the greatest shows alone.
            var type = a.WeaponDamageType is { } damageType ? DamageTypeName(unchecked((uint)damageType)) : "Unknown";
            var range = a.WeaponDamageVariance is { } variance and > 0.0 && variance < 1.0 ? (damage * (1.0 - variance)).ToString("G3", CultureInfo.InvariantCulture) + " - " + damage.ToString(CultureInfo.InvariantCulture) : damage.ToString(CultureInfo.InvariantCulture);
            var damageColor = WeaponColor(a, WeaponDamageBit);
            AddColored(lines, colors, $"Damage: {range}, {type}", damageColor != ExamineColor.Normal ? damageColor : WeaponColor(a, WeaponVarianceBit));
        }

        if (a.Int(ElementalDamageBonusId) is { } elementalBonus and > 0)
        {
            var element = a.WeaponDamageType is { } elementType ? DamageTypeName(unchecked((uint)elementType)) : "Unknown";
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Elemental Damage Bonus: {elementalBonus}, {element}."));
        }

        if (a.WeaponSpeed is { } speed)
        {
            // "Speed: <word> (<time>)" (AppraisalSystem::WeaponTimeToString 0x005B5A20 gives the word); a time below 0 is retail's "Speed:  Unknown", two spaces and all.
            if (speed <= -1)
            {
                lines.Add("Speed:  Unknown");
            }
            else
            {
                AddColored(lines, colors, string.Create(CultureInfo.InvariantCulture, $"Speed: {SpeedWord(speed)} ({speed})"), WeaponColor(a, WeaponSpeedBit));
            }
        }
    }

    /// <summary>The PropertyInt triples of the four wield requirements (type, skill or quality, difficulty), in retail's order: 158-160, 270-272, 273-275, 276-278.</summary>
    private static readonly (uint Type, uint Skill, uint Difficulty)[] WieldSlots = [(158, 159, 160), (270, 271, 272), (273, 274, 275), (276, 277, 278)];

    /// <summary>
    /// <c>Appraisal_ShowWieldRequirements</c> (11.6096 0x004B0040; M5 7 D137 builds every kind): "Wield requires" the craftsman (PropertyString 25, "the original owner") when PropertyBool 85 is set; "Use requires Throne of Destiny." for
    /// PropertyInt 26 of 1; the heritage of PropertyInt 324 by name; then each of the four requirements whose three ints are all present (<c>GetAppraisalStringFromRequirements</c> 0x004ACE40 names the kind): a skill (1, "base " for 2),
    /// an attribute (3, 4), a vital (5, 6), the level (7), a training (8: "trained" or "specialized" before the skill, capitalised in the fourth slot as retail's text is), a society standing (9, 10), a creature type (11, "... type")
    /// and a heritage (12, "... race"). As retail, an unknown kind in the first slot ends the lines (the later slots are not read); an unknown kind in a later slot is skipped. A name the dats do not give leaves its line out with a
    /// named TODO, never a guess.
    /// </summary>
    private static void WieldLines(Appraisal a, List<string> lines, ILogSink log, Func<uint, uint, string?>? enumString)
    {
        if (a.Bool(85) == true)
        {
            lines.Add("Wield requires " + (a.String(25) ?? "the original owner"));
        }

        if (a.Int(26) == 1)
        {
            lines.Add("Use requires Throne of Destiny.");
        }

        if (a.Int(324) is { } heritageSpecific && HeritageName(heritageSpecific, enumString) is { } heritageName)
        {
            lines.Add("Wield requires " + heritageName);
        }

        for (var slot = 0; slot < WieldSlots.Length; slot++)
        {
            var (typeId, skillId, difficultyId) = WieldSlots[slot];
            if (a.Int(typeId) is not { } type || a.Int(skillId) is not { } skill || a.Int(difficultyId) is not { } difficulty)
            {
                continue;
            }

            var line = WieldLine(type, skill, difficulty, capitalTraining: slot == 3, log, enumString);
            if (line is not null)
            {
                lines.Add(line);
            }
            else if (slot == 0 && type is < 1 or > 12)
            {
                Todo(log, "m5-examine-requirement-kind", $"the first wield requirement is of kind {type}, which retail does not name; as retail, no wield requirement is shown");
                return;
            }
        }
    }

    /// <summary>One wield requirement's line (<c>GetAppraisalStringFromRequirements</c> 0x004ACE40 and the line's format), or null when the kind is unknown or the dats give no name for it.</summary>
    internal static string? WieldLine(int type, int skill, int difficulty, bool capitalTraining, ILogSink log, Func<uint, uint, string?>? enumString)
    {
        var basePrefix = type is 2 or 4 or 6 ? "base " : string.Empty;
        string? name = type switch
        {
            1 or 2 or 8 => SkillName(skill),
            3 or 4 => AttributeName(skill),
            5 or 6 => VitalName(skill),
            7 => "level",
            9 or 10 => skill switch { 287 => "Standing with the Celestial Hand", 288 => "Standing with the Eldrytch Web", 289 => "Standing with the Radiant Blood", _ => "unknown quality" },
            11 => CreatureTypeName(difficulty, enumString),
            12 => HeritageName(difficulty, enumString),
            _ => null,
        };
        if (name is null)
        {
            if (type is >= 1 and <= 12)
            {
                Todo(log, "m5-examine-requirement-name", $"a wield requirement of kind {type} names {(type is 11 or 12 ? difficulty : skill)}, which the dats do not name here; the line is left out");
            }

            return null;
        }

        var training = difficulty == 3 ? "specialized" : "trained";
        return type switch
        {
            8 => "Wield requires " + (capitalTraining ? char.ToUpperInvariant(training[0]) + training[1..] : training) + " " + name,
            11 => "Wield requires " + name + " type",
            12 => "Wield requires " + name + " race",
            _ => string.Create(CultureInfo.InvariantCulture, $"Wield requires {basePrefix}{name} {difficulty}"),
        };
    }

    /// <summary>
    /// <c>AppraisalSystem::InqHeritageGroupDisplayName</c> (11.6096 0x005B57C0): retail's own spellings for Gharu'ndim (2), Umbraen (5) and Olthoi (13), else the dat's EnumMapper 0x10000002 (<see cref="Lifestone.Data.Dat.EnumMapperNames.GetString"/>);
    /// null when neither names it.
    /// </summary>
    internal static string? HeritageName(int heritage, Func<uint, uint, string?>? enumString) => heritage switch
    {
        2 => "Gharu'ndim",
        5 => "Umbraen",
        13 => "Olthoi",
        _ => enumString?.Invoke(HeritageEnumMap, unchecked((uint)heritage)) is { Length: > 0 } name ? name : null,
    };

    /// <summary><c>AppraisalSystem::InqCreatureDisplayName</c> (11.6096 0x005B6A90): the dat's EnumMapper 0x10000005 name of the creature type, its underscores as spaces ("Phyntos_Wasp" reads "Phyntos Wasp"); null when it has none.</summary>
    internal static string? CreatureTypeName(int creatureType, Func<uint, uint, string?>? enumString) =>
        enumString?.Invoke(CreatureTypeEnumMap, unchecked((uint)creatureType)) is { Length: > 0 } name ? name.Replace('_', ' ') : null;

    /// <summary>The EnumMapper of the heritage names (<c>InqHeritageGroupDisplayName</c>'s 0x10000002).</summary>
    internal const uint HeritageEnumMap = 0x10000002;

    /// <summary>The EnumMapper of the creature-type names (<c>InqCreatureDisplayName</c>'s 0x10000005).</summary>
    internal const uint CreatureTypeEnumMap = 0x10000005;

    /// <summary>A potion (<c>Appraisal_ShowBoostValue</c> 0x004B2B20: "Restores %d Health when used.") or a healing kit (<c>Appraisal_ShowHealKitValues</c> 0x004B2C40: the skill bonus and the restoration bonus).</summary>
    private static void BoostLines(Appraisal a, List<string> lines)
    {
        var boost = a.HealingBoost;
        if (a.Float(100) is { } mod)
        {
            if (boost is { } skillBonus)
            {
                lines.Add("Bonus to Healing Skill: " + skillBonus.ToString(CultureInfo.InvariantCulture));
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Restoration Bonus: {(long)(mod * 100.0)}%"));
            return;
        }

        if (boost is not { } amount)
        {
            return;
        }

        var (word, when) = a.BoostedVital switch
        {
            2 => ("Health", "used"),
            4 => ("Stamina", "consumed"),
            6 => ("Mana", "used"),
            _ => (string.Empty, string.Empty),
        };
        if (word.Length > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{(amount < 0 ? "Depletes" : "Restores")} {Math.Abs(amount)} {word} when {when}."));
        }
    }

    /// <summary>
    /// A skill's name by its id. Retail reads the skill table's names; this keeps the ones a weapon, a wield requirement or the combat skills name (ACE's skill ids, which the client shares) and answers "skill N" for another with a logged
    /// <c>TODO(m4-examine-skill-names)</c> (the skill table dat is not read here).
    /// </summary>
    internal static string SkillName(int id) => id switch
    {
        1 => "Axe", 2 => "Bow", 3 => "Crossbow", 4 => "Dagger", 5 => "Mace", 6 => "Melee Defense", 7 => "Missile Defense", 8 => "Sling", 9 => "Spear", 10 => "Staff", 11 => "Sword",
        12 => "Thrown Weapon", 13 => "Unarmed Combat", 14 => "Arcane Lore", 15 => "Magic Defense", 16 => "Mana Conversion", 18 => "Item Tinkering", 19 => "Assess Person", 20 => "Deception",
        21 => "Healing", 22 => "Jump", 23 => "Lockpick", 24 => "Run", 27 => "Assess Creature", 28 => "Weapon Tinkering", 29 => "Armor Tinkering", 30 => "Magic Item Tinkering",
        31 => "Creature Enchantment", 32 => "Item Enchantment", 33 => "Life Magic", 34 => "War Magic", 35 => "Leadership", 36 => "Loyalty", 37 => "Fletching", 38 => "Alchemy", 39 => "Cooking",
        40 => "Salvaging", 41 => "Two Handed Combat", 43 => "Void Magic", 44 => "Heavy Weapons", 45 => "Light Weapons", 46 => "Finesse Weapons", 47 => "Missile Weapons", 48 => "Shield",
        49 => "Dual Wield", 50 => "Recklessness", 51 => "Sneak Attack", 52 => "Dirty Fighting", 54 => "Summoning",
        _ => "skill " + id.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary><c>AppraisalSystem::DamageTypeToString</c> (11.6096 0x005B4200): the names of the set bits in bit order, joined by "/" ("Bludgeoning"); "Unknown" for none (retail's ", unknown type").</summary>
    internal static string DamageTypeName(uint damageType)
    {
        ReadOnlySpan<(uint Bit, string Name)> names =
        [
            (0x1, "Slashing"), (0x2, "Piercing"), (0x4, "Bludgeoning"), (0x8, "Cold"), (0x10, "Fire"), (0x20, "Acid"), (0x40, "Electrical"), (0x400, "Nether"), (0x10000000, "Prismatic"),
        ];
        var found = new List<string>(2);
        foreach (var (bit, name) in names)
        {
            if ((damageType & bit) != 0)
            {
                found.Add(name);
            }
        }

        return found.Count == 0 ? "Unknown" : string.Join('/', found);
    }

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Placeholder(string feature, string todo, ILogSink log)
    {
        Todo(log, todo, feature + " is not built yet");
        return feature + ": not in Lifestone M4";
    }

    private static void Todo(ILogSink log, string name, string text) => log.Warn("retailui", $"placeholder: TODO({name}): {text}");

    // --- drawing ----------------------------------------------------------------------------------------------------------------------------------------------------------------

    private void ShowKind(ExamineKind kind)
    {
        _itemField?.SetVisible(kind == ExamineKind.Item);
        _creatureField?.SetVisible(kind == ExamineKind.Creature);
        _spellField?.SetVisible(false);
    }

    private void SetTitle(string text)
    {
        Title = text;
        _title?.SetText(text); // SetTitleText 0x004ADCB0
        _itemName?.SetText(text);
        _itemSubName?.SetText(string.Empty);
    }

    /// <summary>
    /// <c>ItemExamineUI::AddItemInfo</c> (11.6096 0x004AE020): each line appended whole in font 0 and its colour index (<c>AppendTextWithFont</c>; the constructor 0x004ABB70 makes index 1 the raised font and 2 the lowered, M5 7 D142),
    /// a line break between lines in the normal colour.
    /// </summary>
    private void SetItem(in UiItemIcon icon, ExamineItemPage page)
    {
        Lines = page.Lines;
        LineColors = page.Colors;
        IconId = icon.IconId;
        Icon = icon;
        if (_itemText is { } text)
        {
            text.ClearAllText();
            for (var i = 0; i < page.Lines.Count; i++)
            {
                if (i > 0)
                {
                    text.AppendTextWithFont("\n", 0, ExamineColor.Normal);
                }

                text.AppendTextWithFont(page.Lines[i], 0, page.ColorOf(i));
            }
        }

        ComposeIcon();
    }

    private void ComposeIcon()
    {
        if (_itemIcon is null)
        {
            return;
        }

        if (Icon.IconId == 0)
        {
            IconPending = false;
            StopRetry();
            _itemIcon.ClearImage();
            return;
        }

        var image = UiItemIcons.Compose(_context.Manager, Icon, out var pending);
        if (pending)
        {
            IconPending = true;
            if (!_retryRegistered)
            {
                _retryRegistered = true;
                _context.Manager.RegisterForGlobalMessage(_retry, UiGlobalMessageIds.Frame);
            }

            return;
        }

        IconPending = false;
        StopRetry();
        IconCompositions++;
        _itemIcon.SetBlitMode(UiBlitMode.SrcCopy);
        _itemIcon.SetGeneratedImage(image);
    }

    private void StopRetry()
    {
        if (_retryRegistered)
        {
            _retryRegistered = false;
            _context.Manager.UnregisterForGlobalMessage(_retry, UiGlobalMessageIds.Frame);
        }
    }

    /// <summary>Makes the icon element the floaty's merged layout lacks, from the standalone layout 0x2100001C, as a child of the item field; null (logged) when the layout or its element is missing.</summary>
    private UiElement? MakeIconElement(UiElement? field)
    {
        if (field is null)
        {
            return null;
        }

        var manager = _context.Manager;
        var desc = manager.Services.Descs.TryGetLayout(StandaloneLayoutId, out var layout) ? layout.AllElements().FirstOrDefault(static e => e.ElementId == ItemIconId) : null;
        var made = desc is not null ? manager.CreateChildElement(field, layout!, desc) : null;
        if (made is null)
        {
            manager.Services.Log.Warn("retailui", "gmExaminationUI: the standalone layout 0x2100001C gives no icon element 0x1000013B; the item window shows no icon (TODO(m4-examine-icon))");
        }

        return made;
    }

    /// <summary>Retries a composition that found an image still decoding, each frame.</summary>
    private sealed class IconRetry(GameExaminePanel panel) : IUiGlobalListener
    {
        public void ListenToGlobalMessage(uint messageId, int data)
        {
            if (messageId == UiGlobalMessageIds.Frame && panel.IconPending)
            {
                panel.ComposeIcon();
            }
        }
    }
}
