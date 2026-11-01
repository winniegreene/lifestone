using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Panels;

// M5 7 D137 (lane L3 wired): the creature window drawn from ExamineCreatureLines' page, as gmExaminationUI::SetAppraiseInfo (11.6096 0x004ADE60) hands the answer to BasicCreatureExamineUI::SetAppraiseInfo (0x004B4610) and
// CreatureExamineUI's or CharExamineUI's (0x004B4690, 0x004B4EF0): the level 0x1000014C, the nine stat rows of list 0x10000149 and the information rows of list 0x10000335 (each row from the list's template 0, its label
// 0x1000012A and value 0x1000012B written in the row's colour with SetTextWithFont), the creature panel 0x1000014D with its type text 0x1000014E, or the character panel 0x1000014F with its heritage, title and player-killer texts
// (0x10000150 to 0x10000152) and the allegiance name 0x1000053A; a character's full name is the window title 0x1000012D.
public sealed partial class GameExaminePanel
{
    /// <summary>The creature window's creature sub-panel (<c>CreatureExamineUI</c>).</summary>
    public const uint CreaturePanelId = 0x1000014D;

    /// <summary>The creature window's character sub-panel (<c>CharExamineUI</c>).</summary>
    public const uint CharacterPanelId = 0x1000014F;

    /// <summary>The nine stat rows (<c>BasicCreatureExamineUI</c>'s attribute list).</summary>
    public const uint StatsListId = 0x10000149;

    /// <summary>The information rows (<c>BasicCreatureExamineUI::AddLineToMiscInfo</c> 0x004ABF30).</summary>
    public const uint InfoListId = 0x10000335;

    /// <summary>A row's label (the list template's 0x1000012A).</summary>
    public const uint RowLabelId = 0x1000012A;

    /// <summary>A row's value (the list template's 0x1000012B).</summary>
    public const uint RowValueId = 0x1000012B;

    /// <summary>The creature panel's type text (<c>m_creatureDisplayName</c>).</summary>
    public const uint CreatureTypeTextId = 0x1000014E;

    /// <summary>The character panel's gender and heritage, title and player-killer texts.</summary>
    public static readonly uint[] CharacterTextIds = [0x10000150, 0x10000151, 0x10000152];

    /// <summary>The allegiance name text (cleared for a creature).</summary>
    public const uint AllegianceTextId = 0x1000053A;

    private UiListBox? _stats;
    private UiListBox? _info;
    private UiElement? _creaturePanel;
    private UiElement? _characterPanel;
    private UiTextElement? _creatureType;
    private readonly UiTextElement?[] _characterTexts = new UiTextElement?[3];
    private UiTextElement? _allegiance;
    private bool _creatureLooked;

    /// <summary>The creature page the window shows (null before an answer, or for an item).</summary>
    public ExamineCreaturePage? CreaturePage { get; private set; }

    /// <summary>The level text the window shows.</summary>
    public string LevelText { get; private set; } = string.Empty;

    /// <summary>The health, stamina and mana rows as "Label: value" (the stat list's last three rows), for the checks and the log.</summary>
    public IReadOnlyList<string> VitalLines { get; private set; } = [];

    private void FindCreatureElements()
    {
        if (_creatureLooked)
        {
            return;
        }

        _creatureLooked = true;
        _stats = _window.GetChildRecursive(StatsListId) as UiListBox;
        _info = _window.GetChildRecursive(InfoListId) as UiListBox;
        _creaturePanel = _window.GetChildRecursive(CreaturePanelId);
        _characterPanel = _window.GetChildRecursive(CharacterPanelId);
        _creatureType = _window.GetChildRecursive(CreatureTypeTextId) as UiTextElement;
        for (var i = 0; i < CharacterTextIds.Length; i++)
        {
            _characterTexts[i] = _window.GetChildRecursive(CharacterTextIds[i]) as UiTextElement;
        }

        _allegiance = _window.GetChildRecursive(AllegianceTextId) as UiTextElement;
        if (_stats is null || _info is null)
        {
            _context.Manager.LogOnce("examine:creature-lists", LogSeverity.Error, $"gmExaminationUI: the creature window lacks its stat list 0x{StatsListId:X8} or its information list 0x{InfoListId:X8}; their rows are not drawn (TODO(m5-examine-creature-layout))");
        }
    }

    /// <summary>
    /// What the creature window needs besides the answer (<see cref="ExamineCreatureContext"/>): the object's name, its description bits (the player-killer standing, <c>ACCWeenieObject::IsPK</c> 0x0058D6E0 and <c>IsPKLite</c>
    /// 0x0058D6D0), the viewer's society bits (its PropertyInt 281, 0 when absent) and the dats' tables (<see cref="ExamineNameTables"/>: the EnumMapper names and the language dat's strings). A rig without dats gives no tables
    /// (the producer's named TODO).
    /// </summary>
    private ExamineCreatureContext ContextFor(Appraisal appraisal, WorldObject? obj)
    {
        var services = _context.Manager.Services;
        var names = services.EnumNames is { } enumNames ? new ExamineNameTables(enumNames, services.Strings) : null;
        return new ExamineCreatureContext(appraisal.Name ?? obj?.Name ?? string.Empty, obj?.DescriptionFlags ?? 0, ExamineCreatureLines.ViewerFactionBitsOf(_context.Session.State.Self.Qualities), names);
    }

    /// <summary>The window opened on a creature: the level and the values unknown until the answer, no information rows.</summary>
    private void ShowPendingCreature()
    {
        FindCreatureElements();
        CreaturePage = null;
        LevelText = "???";
        VitalLines = ["Health: ???", "Stamina: ???", "Mana: ???"];
        _level?.SetText(LevelText);
        _stats?.Flush();
        _info?.Flush();
        _creatureType?.SetText(string.Empty);
        foreach (var text in _characterTexts)
        {
            text?.SetText(string.Empty);
        }

        _allegiance?.SetText(string.Empty);
    }

    /// <summary>Draws <paramref name="page"/>: the sub-panel of its kind, the level, the two lists' rows in their colours and the texts (a null text is cleared: a fresh window, not retail's left-over text).</summary>
    private void ShowCreaturePage(ExamineCreaturePage page)
    {
        FindCreatureElements();
        CreaturePage = page;
        LevelText = page.Level;
        VitalLines = [.. page.Stats.Skip(Math.Max(0, page.Stats.Count - 3)).Select(static r => r.Label + ": " + r.Value)];
        _level?.SetText(page.Level);
        _creaturePanel?.SetVisible(page.Kind == ExamineCreatureKind.Creature);
        _characterPanel?.SetVisible(page.Kind == ExamineCreatureKind.Character);
        Fill(_stats, page.Stats);
        Fill(_info, page.Info);
        _creatureType?.SetText(page.CreatureTypeName ?? string.Empty);
        string?[] texts = [page.HeritageText, page.ProfessionText, page.PlayerKillerText];
        for (var i = 0; i < _characterTexts.Length; i++)
        {
            _characterTexts[i]?.SetText(texts[i] ?? string.Empty);
        }

        _allegiance?.SetText(page.AllegianceNameText);
        if (page.FullName is { Length: > 0 } fullName)
        {
            SetTitle(fullName);
        }
    }

    private void Fill(UiListBox? list, IReadOnlyList<ExamineRow> rows)
    {
        if (list is null)
        {
            return;
        }

        list.Flush();
        foreach (var row in rows)
        {
            if (list.AddItemFromTemplateList(0) is not { } element)
            {
                _context.Manager.LogOnce($"examine:row:{list.ElementId:X8}", LogSeverity.Error, $"gmExaminationUI: the list 0x{list.ElementId:X8} has no row template; its rows are not drawn (TODO(m5-examine-creature-layout))");
                return;
            }

            Write(element.GetChildRecursive(RowLabelId) as UiTextElement, row.Label, row.Color);
            Write(element.GetChildRecursive(RowValueId) as UiTextElement, row.Value, row.Color);
        }
    }

    // UIElement_Text::SetTextWithFont: the text replaced, in font 0 and the row's colour index.
    private static void Write(UiTextElement? text, string value, int color)
    {
        if (text is null)
        {
            return;
        }

        text.ClearAllText();
        if (value.Length > 0)
        {
            text.AppendTextWithFont(value, 0, color);
        }
    }

    /// <summary>The live proof frame a creature with a full page gives (M5 7 D138, <c>--examine-capture-creatures</c>).</summary>
    public const string CaptureCreature = "creature";

    /// <summary>The live proof frame a failed roll gives.</summary>
    public const string CaptureFailedRoll = "failed-roll";

    /// <summary>The live proof frame an item with an inscription, gear ratings or a set gives.</summary>
    public const string CaptureItemExtras = "item-extras";

    /// <summary>The three proof frames, in the order the log names them.</summary>
    public static IReadOnlyList<string> CaptureKinds { get; } = [CaptureCreature, CaptureFailedRoll, CaptureItemExtras];

    /// <summary>
    /// Which proof frame the window shows now (M5 7 D138), or null: an answered creature page from a successful roll (<see cref="CaptureCreature"/>), a failed one (<see cref="CaptureFailedRoll"/>), or an item whose lines hold a set or
    /// ratings line or whose box holds a written inscription (<see cref="CaptureItemExtras"/>). Null while the window is closed or its answer pending.
    /// </summary>
    public string? CaptureKind()
    {
        if (!IsOpen || Pending || ShownGuid == 0 || ShownSuccess is not { } success)
        {
            return null;
        }

        if (Kind == ExamineKind.Creature && CreaturePage is not null)
        {
            return success ? CaptureCreature : CaptureFailedRoll;
        }

        var extras = Lines.Any(static l => l.StartsWith("Set: ", StringComparison.Ordinal) || l.StartsWith("Ratings: ", StringComparison.Ordinal))
            || (InscriptionShown && SignatureText.Length > 0);
        return Kind == ExamineKind.Item && success && extras ? CaptureItemExtras : null;
    }

    /// <summary>The window's content as one log line (a proof frame's): the title, then a creature's level, stat and information rows and texts, or an item's lines and its inscription.</summary>
    public string CaptureText()
    {
        if (Kind == ExamineKind.Creature && CreaturePage is { } page)
        {
            var rows = string.Join(" | ", page.Stats.Concat(page.Info).Where(static r => !r.IsBlank).Select(static r => $"{r.Label} {r.Value} (c{r.Color})"));
            string?[] texts = [page.CreatureTypeName, page.FullName, page.HeritageText, page.ProfessionText, page.PlayerKillerText, page.AllegianceNameText];
            return $"title=\"{Title}\" level={page.Level} kind={page.Kind} rows=[{rows}] texts=[{string.Join(" | ", texts.Where(static t => !string.IsNullOrEmpty(t)))}]";
        }

        var inscription = InscriptionShown ? $" inscription=\"{InscriptionText}\" signature=\"{SignatureText}\"" : string.Empty;
        return $"title=\"{Title}\" lines=[{string.Join(" | ", Lines.Where(static l => l.Length > 0))}]{inscription}";
    }

    /// <summary>
    /// The creature window's level and vitals as text (a test reads them without a shell; M5 7 D137): one source, <see cref="ExamineCreatureLines.Build"/>'s page, its level and its last three stat rows as "Label: value" ("Health: 55/60
    /// (92 %)", a failed roll's "Health: 74 %" and "Stamina: ???"). An answer with no creature profile shows "???" for each, as the stat rows' values do with nothing to read.
    /// </summary>
    public static (string Level, string[] Vitals, string Note) CreatureLines(Appraisal a, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (ExamineCreatureLines.Build(a, new ExamineCreatureContext(a.Name ?? string.Empty, 0, 0, null), log) is not { } page)
        {
            return (ExamineCreatureLines.LevelText(a), ["Health: ???", "Stamina: ???", "Mana: ???"], string.Empty);
        }

        var vitals = page.Stats.Skip(Math.Max(0, page.Stats.Count - 3)).Select(static r => r.Label + ": " + r.Value).ToArray();
        return (page.Level, vitals, string.Empty);
    }
}
