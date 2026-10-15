using System.Globalization;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// The summary page (retail's <c>gmCGSummaryPage</c>, element 0x100003D6, class 0x1000003E): the character's profession, gender, heritage and start
/// town, its attributes and its skills in a list (0x10000400), the name field (0x10000402) with the sample names under it (the how-to text, 0x10000404),
/// and the character in its own 3D view (viewport 0x10000406). The Finish button of the main screen creates the character from it.
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>gmCGSummaryPage::InitializePage</c> (11.4186 0x0047BBF0, 11.6096 0x0047BFB0), <c>Update</c> (0x0047BAA0, 0x0047BE60),
/// <c>SetSummaryText</c> (0x0047B1D0, 0x0047B590), <c>SetHowToText</c> (0x0047AE20, 0x0047B1E0), <c>DoNameLimitDialog</c> (0x0047BD80, 0x0047C140) and
/// <c>ListenToElementMessage</c> (0x0047BF40, 0x0047C300). Opening the page fits the profession to the character (<c>FitTemplateToCharacter</c>), builds
/// the list and the how-to text, composes the 3D character, gives the name field the keyboard and, until a name has been typed, fills it with the
/// <c>ID_CharGen_NamePrompt</c> string, selected so the first letter replaces it.
/// </para>
/// <para>
/// <b>The name.</b> The field takes only letters, apostrophes, spaces and hyphens (retail's <c>NameInputFilter</c>, <see cref="TextFilters.Name"/>). Each
/// typed or deleted character marks the name entered and, when the field is not empty, hands its text to <see cref="CharGenState.SetName"/> (which formats
/// it as <c>ACCharGenData::FormatName</c> does); a text of more than 32 characters is taken back to the state's name and <c>ID_CharGen_NameTooLong</c>
/// says so.
/// </para>
/// <para>
/// <b>Names retail takes from the exe.</b> The list's profession, gender, heritage and town names come from four arrays of the client
/// (<c>pcProfessions</c>, <c>pcGender</c>, <c>pcHeritage</c>, <c>pcTown</c>) whose contents the decompile does not show (its data header gives only each
/// array's first entry: "Custom", "?"). Lifestone takes the names from the CharGen data instead (the template's, the gender's, the heritage group's and the
/// start area's), except the custom profession, whose retail name is read ("Custom"; the data calls template 0 "Adventurer"); the data's words are the
/// ones retail's arrays most likely hold (the tests compare what they can); <c>TODO(retailui-chargen-summary-data)</c> is logged until the arrays are read
/// from the exe. Retail names only heritages 1 to 4 (the page predates the others) and leaves "Heritage: " empty for the rest, a glitch:
/// Lifestone names every heritage, and <c>--retail-ui-glitches</c> (<see cref="UiRetailGlitches.CharGenSummaryHeritage"/>) restores the empty line.
/// Retail also hands the name to the state only when the field is not empty, so a name deleted from the field is still the state's and Finish creates the
/// character under it, another glitch: Lifestone clears the state's name (Finish then says there is no name), and the flag
/// (<see cref="UiRetailGlitches.CharGenEmptyName"/>) restores retail's.
/// </para>
/// </remarks>
public sealed class CharGenSummaryPage : CharGenPage, IUiGlobalListener
{
    /// <summary>The list of attributes and skills.</summary>
    public const uint ListId = 0x10000400;

    /// <summary>The name field.</summary>
    public const uint NameId = 0x10000402;

    /// <summary>The how-to text with the sample names.</summary>
    public const uint HowToId = 0x10000404;

    /// <summary>List template 0: one line of text (child <see cref="LineTextId"/>).</summary>
    public const int LineTemplate = 0;

    /// <summary>List template 1: a heading (child <see cref="HeaderTextId"/>).</summary>
    public const int HeaderTemplate = 1;

    /// <summary>List template 2: a name and a value (children <see cref="PairNameId"/> and <see cref="PairValueId"/>).</summary>
    public const int PairTemplate = 2;

    /// <summary>The text of a line row.</summary>
    public const uint LineTextId = 0x100002F9;

    /// <summary>The text of a heading row.</summary>
    public const uint HeaderTextId = 0x100000FE;

    /// <summary>The name of a pair row.</summary>
    public const uint PairNameId = 0x100002FC;

    /// <summary>The value of a pair row.</summary>
    public const uint PairValueId = 0x100002FD;

    private readonly UiListBox? _list;
    private readonly UiTextElement? _name;
    private readonly UiTextElement? _howTo;
    private readonly Cg3DViewModel? _view;
    private readonly IDisposable? _attachment;
    private bool _nameEntered;
    private uint _errorContext;

    /// <summary>The summary page over <paramref name="root"/>: its elements found, the 3D view made and shown if the page is.</summary>
    public CharGenSummaryPage(CharGenMainScreen main, UiElement root)
        : base(main, root, "gmCGSummaryPage")
    {
        _list = Find<UiListBox>(ListId);
        _name = Find<UiTextElement>(NameId);
        if (_name is not null)
        {
            _name.Filter = TextFilters.Name;
        }

        _howTo = Find<UiTextElement>(HowToId);
        Find<UiScrollbar>(0x10000401);
        if (Find<UiViewport>(CharGenMainScreen.SummaryViewportId) is not null)
        {
            _view = Main.NewView3D();
            if (_view is not null)
            {
                _view.SetCamera(CharGenRules.BodyCamera(Main.State.Heritage), default);
                _view.SetPlayerHeading(CreaturePreview.CharGenHeading);
                _view.StartAnimation();
                _attachment = Main.AttachView(CharGenMainScreen.SummaryViewportId, _view);
            }
        }

        Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        if (Main.View3DData is null)
        {
            Manager.LogOnce(
                "chargen:summary-view",
                LogSeverity.Warning,
                "TODO(retailui-chargen-view): the host gave character creation no 3D view data (CharGenSetup.View3D); the summary page has no character (placeholder)");
        }

        Manager.LogOnce(
            "chargen:summary-data",
            LogSeverity.Warning,
            "TODO(retailui-chargen-summary-data): the summary page's profession, gender, heritage and town names are four arrays of the client (pcProfessions, pcGender, pcHeritage, pcTown) that the decompile does not show; the CharGen data's names stand in");
        if (Root.Visible)
        {
            Update();
        }
    }

    /// <summary>The list of attributes and skills.</summary>
    public UiListBox? List => _list;

    /// <summary>The name field.</summary>
    public UiTextElement? NameField => _name;

    /// <summary>The how-to text now shown (the sample names).</summary>
    public string HowToText => _howTo?.Text ?? string.Empty;

    /// <summary>The page's 3D character, or null when the host gave none.</summary>
    public Cg3DViewModel? View => _view;

    /// <summary>True once something has been typed or deleted in the name field (<c>m_bNameEntered</c>): Finish needs it.</summary>
    public bool NameEntered => _nameEntered;

    /// <summary>The context of the open "name too long" message, or 0.</summary>
    public uint NameLimitDialog => _errorContext;

    /// <inheritdoc/>
    public override string? PlaceholderText => _view is null ? "placeholder: the 3D character view needs the CharGen view data, which this host did not give" : null;

    /// <summary>
    /// <c>gmCGSummaryPage::Update</c>: when the page shows, the camera by heritage, the profession fitted to the character, the list and the how-to text
    /// built, the 3D character composed, the name field given the keyboard and, while no name has been typed, the prompt in it, selected.
    /// </summary>
    public override void Update()
    {
        if (!Root.Visible)
        {
            return;
        }

        var state = Main.State;
        _view?.SetCamera(CharGenRules.BodyCamera(state.Heritage), default);
        state.FitTemplateToCharacter();
        SetSummaryText();
        SetHowToText();
        _view?.Update(Manager.Now);
        _name?.TakeFocus();
        if (!_nameEntered && _name is not null)
        {
            _name.SetStringInfo(Pregame("ID_CharGen_NamePrompt"));
            _name.SelectAll();
        }
    }

    /// <summary>
    /// <c>gmCGSummaryPage::SetSummaryText</c>: the list rebuilt: the profession, gender, heritage and start town as lines; the attributes (with health,
    /// stamina, mana and the skill credits left) as pairs under a heading; then the specialised, trained, useable untrained and unuseable untrained skills,
    /// each skill in the state's name order with its score.
    /// </summary>
    public void SetSummaryText()
    {
        if (_list is null)
        {
            return;
        }

        var state = Main.State;
        var catalog = Main.Catalog;
        _list.Flush();
        catalog.Data.TryGetHeritage(state.Heritage, out var heritage);
        AddLine("Profession: " + ProfessionName(state.Template, heritage));
        var genderName = heritage is not null && heritage.TryGetGender(state.Gender, out var sex) ? sex.Name : string.Empty;
        AddLine("Gender: " + (state.Gender == 0 ? "?" : state.Gender <= 2 ? genderName : string.Empty));
        var retailGlitches = (Manager.Services.RetailGlitches & UiRetailGlitches.CharGenSummaryHeritage) != 0;
        AddLine("Heritage: " + (heritage is not null && (state.Heritage <= 4 || !retailGlitches) ? heritage.Name : string.Empty));
        var areas = catalog.Data.StartAreas;
        AddLine("Starting Town: " + (state.StartArea >= 0 && state.StartArea <= 3 && state.StartArea < areas.Count ? areas[state.StartArea].Name : string.Empty));
        AddHeader("Attributes");
        AddPair("Strength", state.Strength);
        AddPair("Endurance", state.Endurance);
        AddPair("Coordination", state.Coordination);
        AddPair("Quickness", state.Quickness);
        AddPair("Focus", state.Focus);
        AddPair("Self", state.Self);
        AddPair("Health", CharGenRules.HealthOf(state.GetAttribute(2)));
        AddPair("Stamina", state.GetAttribute(2));
        AddPair("Mana", state.GetAttribute(6));
        AddPair("Skill Credits", state.RemainingSkillCredits);
        foreach (var group in CharGenSummaryGroups)
        {
            AddHeader(group.Title);
            foreach (var skill in state.SkillsByName)
            {
                if (CharGenRules.GroupOf(state.GetSkillLevel(skill.Id), skill.MinLevel) == group.Group && state.GetSkillLevel(skill.Id) != SkillAdvancementClass.Inactive)
                {
                    AddPair(skill.Name, (int)state.GetSkillScore(skill.Id));
                }
            }
        }
    }

    /// <summary>
    /// The profession's name as the summary words it: template 0, the custom one, is "Custom" (the first entry of retail's <c>pcProfessions</c>, the only one the
    /// decompile shows); the others are the heritage's own template names. Empty for a template outside 0 to 6 (retail's list has none there).
    /// </summary>
    public static string ProfessionName(int template, Lifestone.Data.CharacterCreation.CharGenHeritage? heritage)
    {
        if (template == 0)
        {
            return "Custom";
        }

        return template is > 0 and <= 6 && heritage is not null && template < heritage.Templates.Count ? heritage.Templates[template].Name : string.Empty;
    }

    /// <summary>
    /// <c>gmCGSummaryPage::SetHowToText</c>: the how-to line, the sample names for the heritage and gender, and the closing line, as one text (the text is
    /// left as it was unless all three strings exist).
    /// </summary>
    public void SetHowToText()
    {
        var state = Main.State;
        if (_howTo is null || CharGenRules.NamesToken(state.Heritage, state.Gender) is not { } names)
        {
            return;
        }

        _howTo.SetStringInfoWithFont(Pregame("ID_CharGen_SummaryHowTo"), 0, 0);
        _howTo.AppendStringInfoWithFont(Pregame(names), 0, 0);
        _howTo.AppendStringInfoWithFont(Pregame("ID_CharGen_SummaryHowToEnd"), 0, 0);
    }

    private static readonly (string Title, CharGenSkillGroup Group)[] CharGenSummaryGroups =
    [
        ("Specialized Skills", CharGenSkillGroup.Specialized),
        ("Trained Skills", CharGenSkillGroup.Trained),
        ("Useable Untrained Skills", CharGenSkillGroup.UsableUntrained),
        ("Unuseable Untrained Skills", CharGenSkillGroup.UnusableUntrained),
    ];

    /// <summary>The text of every row the list holds, in order (a pair as "name: value"); the checks read it.</summary>
    public IReadOnlyList<string> Rows => _list is null
        ? []
        : [.. _list.Items.Select(RowText)];

    private static string RowText(UiElement row)
    {
        if (row.GetChildRecursive(PairNameId) is UiTextElement name && row.GetChildRecursive(PairValueId) is UiTextElement value)
        {
            return name.Text + ": " + value.Text;
        }

        return (row.GetChildRecursive(LineTextId) as UiTextElement ?? row.GetChildRecursive(HeaderTextId) as UiTextElement)?.Text ?? string.Empty;
    }

    private void AddLine(string text) => Row(LineTemplate, LineTextId)?.SetText(text);

    private void AddHeader(string text) => Row(HeaderTemplate, HeaderTextId)?.SetText(text);

    private void AddPair(string name, int value)
    {
        if (_list?.AddItemFromTemplateList(PairTemplate) is { } row
            && row.GetChildRecursive(PairNameId) is UiTextElement nameText
            && row.GetChildRecursive(PairValueId) is UiTextElement valueText)
        {
            nameText.SetText(name);
            valueText.SetText(value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private UiTextElement? Row(int template, uint textId) =>
        _list?.AddItemFromTemplateList(template)?.GetChildRecursive(textId) as UiTextElement;

    /// <inheritdoc/>
    protected override UiListenResult OnElementMessage(in UiElementMessage message)
    {
        if (message.ElementId == NameId && message.MessageId is UiMessageIds.CharacterTyped or UiMessageIds.TextDeleted && _name is not null)
        {
            _nameEntered = true;
            var text = _name.Text;
            if (text.Length != 0)
            {
                if (text.Length > CharGenNames.MaxLength)
                {
                    _name.SetText(Main.State.Name);
                    DoNameLimitDialog();
                }
                else
                {
                    Main.State.SetName(text);
                }
            }
            else if ((Manager.Services.RetailGlitches & UiRetailGlitches.CharGenEmptyName) == 0)
            {
                // Retail leaves the state's name as it was (UiRetailGlitches.CharGenEmptyName), so Finish creates a character under a name the empty field does not show.
                Main.State.SetName(string.Empty);
            }
        }

        return UiListenResult.Default;
    }

    /// <summary><c>gmCGSummaryPage::DoNameLimitDialog</c>: the message that says a name has at most 32 characters, once at a time.</summary>
    public void DoNameLimitDialog()
    {
        if (_errorContext != 0)
        {
            return;
        }

        var data = UiDialogData.Of(UiDialogType.Message, Pregame("ID_CharGen_NameTooLong")).WithFlag(UiPropertyIds.DialogModal, true);
        _errorContext = Manager.Dialogs.MakeCallbackDialog(data, _ => _errorContext = 0);
    }

    /// <summary>Each frame: the character is stepped to the clock (a still character has nothing to step).</summary>
    void IUiGlobalListener.ListenToGlobalMessage(uint messageId, int data)
    {
        if (!IsDisposed && messageId == UiGlobalMessageIds.Frame && Root.Visible)
        {
            _view?.Advance(Manager.Now);
        }
    }

    /// <inheritdoc/>
    protected override void OnDisposing()
    {
        Manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        _attachment?.Dispose();
        Manager.Dialogs.CloseDialog(_errorContext);
        _errorContext = 0;
    }
}
