using System.Globalization;
using System.Runtime.CompilerServices;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
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
/// (<c>TODO(m4-examine-creature-view)</c>), the item icon (<c>TODO(m4-examine-icon)</c>: the floaty's layout has no icon element), the creature's player-killer, allegiance and rating lines and its attribute list (<c>TODO(m4-examine-creature-lines)</c>), the inscription (<c>TODO(m4-examine-inscription)</c>), the spells
/// and enchantments (<c>TODO(m4-examine-spells)</c>), the ratings and sets (<c>TODO(m4-examine-ratings)</c>), the workmanship and speed words (<c>TODO(m4-examine-words)</c>), a requirement of a kind other than a skill, a level or a
/// training (<c>TODO(m4-examine-requirement)</c>), the toolbar's Examine button (still the NotInM3 message), and the poll that re-asks while the window stays open outside peace (<c>gmExaminationUI::UseTime</c> 0x004AB8B0,
/// <c>TODO(m3-examine-poll-mode)</c>). A failed creature roll shows what ACE sends (the tables, no profile) and a line naming <c>TODO(m4-examine-failed-roll-text)</c>: the docs do not give retail's text for it.
/// </para>
/// </remarks>
public sealed class GameExaminePanel : IUiElementListener, IDisposable
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

    /// <summary>The item window's icon: the standalone layout 0x2100001C has it (244,12, 32 by 32), but the floaty's merged layout the gameplay layout uses has none (retail's <c>m_itemIcon</c> is then null: <c>ItemExamineUI::SetAppraiseInfo</c> 0x004B7ED0 skips it), so nothing draws it (<c>TODO(m4-examine-icon)</c>); <see cref="IconId"/> still names the icon.</summary>
    public const uint ItemIconId = 0x1000013B;

    /// <summary>The item window's text of lines (<c>m_itemDisplayText</c>, with its scroll bar 0x1000013D).</summary>
    public const uint ItemTextId = 0x1000013C;

    /// <summary>The item window's inscription text.</summary>
    public const uint InscriptionId = 0x1000013E;

    /// <summary>The creature window's level value (<c>m_levelValueText</c>).</summary>
    public const uint LevelTextId = 0x1000014C;

    /// <summary>The creature window's three vitals lines (the sub-panel 0x1000014F holds them).</summary>
    public static readonly uint[] VitalLineIds = [0x10000150, 0x10000151, 0x10000152];

    /// <summary>The creature window's character sub-panel text (<c>CharExamineUI</c>, 0x1000014E).</summary>
    public const uint CreatureInfoId = 0x1000014E;

    /// <summary>The creature window's note line (<c>CharExamineUI</c>'s fourth text, 0x1000053A).</summary>
    public const uint CreatureNoteId = 0x1000053A;

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
    private readonly UiTextElement? _itemText;
    private readonly UiTextElement? _level;
    private readonly UiTextElement?[] _vitals = new UiTextElement?[3];
    private readonly UiTextElement? _creatureInfo;
    private readonly UiTextElement? _creatureNote;
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
        _itemIcon = window.GetChildRecursive(ItemIconId);
        _itemText = window.GetChildRecursive(ItemTextId) as UiTextElement;
        _level = window.GetChildRecursive(LevelTextId) as UiTextElement;
        for (var i = 0; i < _vitals.Length; i++)
        {
            _vitals[i] = window.GetChildRecursive(VitalLineIds[i]) as UiTextElement;
        }

        _creatureInfo = window.GetChildRecursive(CreatureInfoId) as UiTextElement;
        _creatureNote = window.GetChildRecursive(CreatureNoteId) as UiTextElement;
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

    /// <summary>The creature window's level text now.</summary>
    public string LevelText { get; private set; } = string.Empty;

    /// <summary>The creature window's vitals lines now.</summary>
    public IReadOnlyList<string> VitalLines { get; private set; } = [];

    /// <summary>The icon the item window shows (0 for none).</summary>
    public uint IconId { get; private set; }

    /// <summary>The last Identify <see cref="Open"/> sent, or null.</summary>
    public IdentifyHandle? LastIdentify { get; private set; }

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
        ShownGuid = guid;
        Pending = true;
        Kind = obj is not null && (obj.ItemType & CreatureItemType) != 0 ? ExamineKind.Creature : ExamineKind.Item;
        SetTitle(obj?.Name is { Length: > 0 } name ? name : "???");
        IconId = obj?.ItemFields().IconId ?? 0;
        ShowKind(Kind);
        if (Kind == ExamineKind.Item)
        {
            SetItem(IconId, ["Value: ???", "Burden: ???"]);
        }
        else
        {
            SetCreature("???", ["???", "???", "???"], string.Empty);
        }

        _window.SetVisible(true);
        _subscription ??= session.Events.Subscribe<AppraisalReceived>(OnAppraisal);
        LastIdentify = session.Actions.Identify(guid, ActionSource.Ui);
        Opens++;
        return true;
    }

    /// <summary>Hides the window and stops listening to the session.</summary>
    public void Close()
    {
        _window.SetVisible(false);
        _subscription?.Dispose();
        _subscription = null;
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
        var session = _context.Session;
        WorldObject? obj = session.State.Objects.TryGet(ShownGuid, out var found) ? found : null;
        Pending = false;
        Fills++;
        var name = appraisal.Name is { Length: > 0 } n ? n : obj?.Name is { Length: > 0 } on ? on : "???";
        SetTitle(name);
        var creature = appraisal.Creature is not null || (appraisal.ItemType is { } type ? (type & CreatureItemType) != 0 : obj is not null && (obj.ItemType & CreatureItemType) != 0);
        Kind = creature ? ExamineKind.Creature : ExamineKind.Item;
        ShowKind(Kind);
        var log = _context.Manager.Services.Log;
        if (creature)
        {
            var (level, vitals, note) = CreatureLines(appraisal, log);
            SetCreature(level, vitals, note);
        }
        else
        {
            IconId = obj?.ItemFields().IconId is { } icon and not 0 ? icon : appraisal.DataId(8) ?? 0;
            SetItem(IconId, ItemLines(appraisal, log));
        }
    }

    // --- the lines (static: a test reads them without a shell) ----------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>ItemExamineUI::SetAppraiseInfo</c> (11.6096 0x004B7ED0): the item window's lines in retail's order: value, burden, workmanship, the weapon's and armour's data, the armour level, the usage, the wield requirements, the potion's or kit's
    /// boost, the uses left, the sale rule, the spells and the description.
    /// </summary>
    public static IReadOnlyList<string> ItemLines(Appraisal a, ILogSink log)
    {
        var lines = new List<string>(12);
        lines.Add(a.Value is { } value ? "Value: " + Number(value) : "Value: ???"); // Appraisal_ShowValueInfo 0x004AE220
        lines.Add(a.Burden is { } burden ? "Burden: " + Number(burden) : "Burden: ???"); // Appraisal_ShowBurdenInfo 0x004AE500
        if (a.Workmanship is { } workmanship)
        {
            lines.Add("Workmanship: " + workmanship.ToString(CultureInfo.InvariantCulture)); // Appraisal_ShowTinkeringInfo 0x004B1510 ("Workmanship: %s (%d)": the word is not built)
            Todo(log, "m4-examine-words", "the workmanship and speed words (the skill-check and workmanship string tables) are not read; the number shows alone");
        }

        if (a.Weapon is not null || a.WeaponDamage is not null)
        {
            WeaponLines(a, lines, log);
        }

        if (a.ArmorLevel is { } armorLevel)
        {
            lines.Add("Armor Level: " + armorLevel.ToString(CultureInfo.InvariantCulture)); // Appraisal_ShowArmorMods 0x004B2600
        }

        if (a.UseText is { Length: > 0 } use)
        {
            lines.Add(use); // Appraisal_ShowUsage 0x004AFFD0
        }

        WieldLines(a, lines, log);
        if (a.HealingBoost is not null || a.Float(100) is not null)
        {
            BoostLines(a, lines);
        }

        if (a.UsesLeft is { } left)
        {
            lines.Add("Number of uses remaining: " + left.ToString(CultureInfo.InvariantCulture)); // Appraisal_ShowRemainingUses 0x004B40C0
        }

        if (a.Bool(69) == false)
        {
            lines.Add("This item cannot be sold.");
        }

        if (a.SpellBook.Length > 0)
        {
            lines.Add(Placeholder("Spells", "m4-examine-spells", log)); // Appraisal_ShowShortMagicInfo 0x004B3330, ShowMagicInfo 0x004B34B0
        }

        if (a.String(7) is { Length: > 0 })
        {
            lines.Add(Placeholder("Inscription", "m4-examine-inscription", log)); // SetInscription 0x004AE670
        }

        if (a.Ints.Keys.Any(static k => k is 307 or 308 or 309 or 310 or 311 or 312 or 313 or 314 or 315) || a.Ints.ContainsKey(170))
        {
            lines.Add(Placeholder("Ratings and set", "m4-examine-ratings", log)); // Appraisal_ShowRatings 0x004AF400, ShowSet 0x004AEC00
        }

        if (a.LongDescription is { Length: > 0 } description)
        {
            lines.Add(description); // Appraisal_ShowDescription 0x004B75B0
        }

        return lines;
    }

    private static void WeaponLines(Appraisal a, List<string> lines, ILogSink log)
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
            lines.Add($"Damage: {range}, {type}");
        }

        if (a.WeaponSpeed is { } speed)
        {
            lines.Add("Speed: " + speed.ToString(CultureInfo.InvariantCulture));
            Todo(log, "m4-examine-words", "the weapon speed word is not read; the number shows alone");
        }
    }

    private static void WieldLines(Appraisal a, List<string> lines, ILogSink log)
    {
        foreach (var requirement in a.WieldRequirements)
        {
            switch (requirement.Type)
            {
                case 1 or 2:
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"Wield requires {SkillName(requirement.Skill)} {requirement.Difficulty}")); // Appraisal_ShowWieldRequirements 0x004B0040
                    break;
                case 7:
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"Wield requires level {requirement.Difficulty}"));
                    break;
                case 8:
                    lines.Add($"Wield requires {SkillName(requirement.Skill)} {(requirement.Difficulty >= 3 ? "specialized" : "trained")}");
                    break;
                default:
                    lines.Add(Placeholder("A wield requirement", "m4-examine-requirement", log));
                    break;
            }
        }
    }

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

    /// <summary>The creature window: the level (<c>BasicCreatureExamineUI::SetLevelValueText</c> 0x004B4510 from property 25) and the vitals.</summary>
    public static (string Level, string[] Vitals, string Note) CreatureLines(Appraisal a, ILogSink log)
    {
        var level = a.Level is { } l ? l.ToString(CultureInfo.InvariantCulture) : "???";
        var vitals = new[] { "Health: ???", "Stamina: ???", "Mana: ???" };
        var note = string.Empty;
        if (a.Creature is not null && a.CreatureHealth is { } health && a.CreatureHealthMax is { } healthMax)
        {
            vitals[0] = string.Create(CultureInfo.InvariantCulture, $"Health: {health} / {healthMax}");
            if (a.CreatureStamina is { } stamina && a.CreatureStaminaMax is { } staminaMax)
            {
                vitals[1] = string.Create(CultureInfo.InvariantCulture, $"Stamina: {stamina} / {staminaMax}");
            }

            if (a.CreatureMana is { } mana && a.CreatureManaMax is { } manaMax)
            {
                vitals[2] = string.Create(CultureInfo.InvariantCulture, $"Mana: {mana} / {manaMax}");
            }
        }
        else if (!a.Success)
        {
            note = Placeholder("The failed assessment", "m4-examine-failed-roll-text", log);
        }

        Todo(log, "m4-examine-creature-view", "the creature's 3D view (the viewport 0x10000148) is not drawn");
        Todo(log, "m4-examine-creature-lines", "the creature's attribute list, rating and player-killer lines (BasicCreatureExamineUI/CharExamineUI) are not built; the three vitals lines stand in for the extra-info list");
        return (level, vitals, note);
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

    private void SetItem(uint icon, IReadOnlyList<string> lines)
    {
        Lines = lines;
        IconId = icon;
        _itemText?.SetText(string.Join('\n', lines));
        if (_itemIcon is not null)
        {
            if (icon != 0)
            {
                _itemIcon.SetImageByDid(icon, UiDrawMode.AlphaBlend);
            }
            else
            {
                _itemIcon.ClearImage();
            }
        }
    }

    private void SetCreature(string level, IReadOnlyList<string> vitals, string note)
    {
        LevelText = level;
        VitalLines = vitals;
        _level?.SetText(level);
        for (var i = 0; i < _vitals.Length; i++)
        {
            _vitals[i]?.SetText(i < vitals.Count ? vitals[i] : string.Empty);
        }

        _creatureInfo?.SetText(string.Empty);
        _creatureNote?.SetText(note);
    }
}
