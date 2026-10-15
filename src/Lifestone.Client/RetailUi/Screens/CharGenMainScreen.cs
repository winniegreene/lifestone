using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// Character creation (retail's <c>gmCharGenMainUI</c>): layout 0x10000039 (<c>chargen_master</c>), root 0x100003CC. Six pages one at a time (heritage,
/// profession, skills, appearance, town and summary; <see cref="CharGenPageId"/>), a progress bar of one tab each (0x100003EF to 0x100003F4), Back
/// (0x100003C6) and Next (0x100003C7) buttons, Finish (0x100003C8, in Next's place on the summary page), Help (0x100003C9), Exit (0x100003CA) and Random
/// (0x100003CB), all over one <see cref="CharGenState"/> that opens as a random character. Reached from character management's Create button
/// (<c>UiModes.CharGen</c>, 0x1000000B).
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>gmCharGenMainUI::gmCharGenMainUI</c> (11.4186 0x004E7EB0, 11.6096 0x004E8B40), <c>Create</c> (0x004E83E0, 0x004E9070),
/// <c>SetProgressState</c> (0x004E7A10, 0x004E86A0), <c>DoRandom</c> (0x004E7D70, 0x004E8A00), <c>DoExit</c> (0x004E8650, 0x004E92E0),
/// <c>MakeRandomizeWarningDialog</c> (0x004E8A90, 0x004E9720), <c>MakeToDWarningDialog</c> (0x004E8E40, 0x004E9AD0), <c>CloseRandomizeWarningDialog</c>
/// (0x004E8400, 0x004E9090), <c>ListenToGlobalMessage</c> (0x004E9000, 0x004E9C90), <c>ListenToElementMessage</c> (0x004E9450, 0x004EA0E0) and
/// <c>RecvNotice_CloseDialog</c> (0x004E9780, 0x004EA410).
/// </para>
/// <para>
/// <b>Navigation.</b> A tab, Back or Next asks for a page (<see cref="SetProgressState"/>): every page hides, the tabs go to Normal, the master page
/// takes the page's state (0x10000025 to 0x1000002A), the page and its tab (state Highlight) show, and the page draws itself again from the state.
/// Back on the first page asks "are you sure" (the exit warning) and then returns to character management; Next does nothing on the last page, where
/// Finish takes its place. Retail never ghosts Back or Next: what a page lets the player do is limited by its own controls (the credits, the
/// locks, the Throne of Destiny check), not by the buttons. The Olthoi have no profession, skills or town page: their tabs are hidden and the page
/// order skips them (<see cref="CharGenRules.Resolve"/>).
/// </para>
/// <para>
/// <b>Finish and the create.</b> Finish (<c>DoFinish</c>, 11.4186 0x004E9170, 11.6096 0x004E9E00) trims the name of "[", "]" and spaces and keeps it; with no
/// name typed it says so (<c>ID_CharGen_NoNameWarning</c>); with attribute credits left it asks first (<c>ID_CharGen_CreditWarning</c>); then the
/// character is checked (<see cref="CharGenVerifier"/>) and sent as the session's create intent (<see cref="IActionApi.CreateCharacter"/>). The server's
/// answer (<c>RecvNotice_CharGenVerificationResponse</c>, 0x004E9030, 0x004E9CC0) is said in retail's words: the name reserved (3), banned (4), refused to
/// the account (7), or the database down (every other result, 2, 5 and 6 included); a request that was not sent at all is said in Lifestone's. On success
/// retail looks the new character up in its list when the list next changes (<c>Update</c>, 0x004E8460, 0x004E90F0) and logs it in
/// (<c>CPlayerSystem::LogOnCharacter</c>): Lifestone sends the Enter action for it at once and shows character management with the new character selected
/// until the player arrives (<see cref="Update"/>).
/// </para>
/// <para>
/// <b>Not built yet.</b> The Help button and key open a "Not in Lifestone M2" dialog (retail opens its help), which logs a TODO. A page whose 3D character
/// view the host gave no data for says so on screen through <see cref="PlaceholderNote"/>.
/// </para>
/// </remarks>
public sealed class CharGenMainScreen : UiLoginScreen, IUiGlobalListener, IUiPlaceholderScreen
{
    /// <summary>The progress bar (the six tabs are its buttons).</summary>
    public const uint ProgressBarId = 0x100003CE;

    /// <summary>The bar of buttons along the bottom.</summary>
    public const uint MainMenuId = 0x100003CF;

    /// <summary>The master page that holds the six pages (its state follows the page).</summary>
    public const uint MasterPageId = 0x100003D0;

    /// <summary>Back.</summary>
    public const uint LeftButtonId = 0x100003C6;

    /// <summary>Next.</summary>
    public const uint RightButtonId = 0x100003C7;

    /// <summary>Finish.</summary>
    public const uint FinishButtonId = 0x100003C8;

    /// <summary>Help.</summary>
    public const uint HelpButtonId = 0x100003C9;

    /// <summary>Exit.</summary>
    public const uint ExitButtonId = 0x100003CA;

    /// <summary>Random.</summary>
    public const uint RandomButtonId = 0x100003CB;

    /// <summary>The viewport element of the appearance page: the hole its 3D character view is drawn in (the host binds it).</summary>
    public const uint AppearanceViewportId = 0x100003BB;

    /// <summary>The viewport element of the summary page (its own 3D character view).</summary>
    public const uint SummaryViewportId = 0x10000406;

    /// <summary>The Viamontian heritage, which needs the Throne of Destiny.</summary>
    public const uint ViamontianHeritage = 4;

    /// <summary>The key action that asks to leave (<c>ListenToGlobalMessage</c> tests action 39, the Escape key).</summary>
    public const uint ExitAction = 39;

    /// <summary>The key action that opens the help (action 123).</summary>
    public const uint HelpAction = 123;

    private readonly CharGenSetup _setup;
    private readonly ICharGenRandom _dice;
    private readonly Dictionary<CharGenPageId, CharGenPage> _pages = [];
    private readonly Dictionary<CharGenPageId, UiButton> _tabs = [];
    private readonly UiElement? _master;
    private readonly UiButton? _left;
    private readonly UiButton? _right;
    private readonly UiButton? _finish;
    private readonly List<IDisposable> _subscriptions = [];
    private bool _verificationPending;
    private bool _awaitingCharacterSet;
    private uint _createdGuid;
    private uint _creditContext;
    private uint _errorContext;
    private uint _noNameContext;
    private uint _exitContext;
    private uint _randomizeContext;
    private uint _throneContext;
    private uint _helpContext;
    private string? _note;

    /// <summary>Character creation over <paramref name="context"/>: a random character, the pages made and the heritage page shown.</summary>
    public CharGenMainScreen(UiLoginContext context)
        : base(context, UiLayoutEnums.CharGenRoot, "gmCharGenMainUI")
    {
        _setup = context.CharGen ?? throw new ArgumentException("character creation needs the CharGen setup of the context", nameof(context));
        _dice = _setup.NewRandom();
        Manager.LogOnce(
            "chargen:11-6096-assumptions",
            LogSeverity.Warning,
            "TODO(retailui-chargen-11-6096): CharGenNames assumes 11.6096 keeps 11.4186's CharCase values (UPPER 0, LOWER 1, EITHER 2) and CharGenState assumes it keeps startAtrb = 1 (Strength); the 11.6096 data is not read");
        State = new CharGenState(_setup.Catalog, _dice);
        State.RandomizeCharacter(HasThroneOfDestiny);
        Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.ActionStart);
        _subscriptions.Add(Session.Events.Subscribe<CharacterCreateResult>(OnCreateResult));
        if (Root is null)
        {
            return;
        }

        foreach (var page in Enum.GetValues<CharGenPageId>())
        {
            if (Find<UiButton>(CharGenRules.TabElementId(page)) is { } tab)
            {
                _tabs[page] = tab;
            }
        }

        _master = Find<UiElement>(MasterPageId);
        _left = Find<UiButton>(LeftButtonId);
        _right = Find<UiButton>(RightButtonId);
        _finish = Find<UiButton>(FinishButtonId);
        Find<UiButton>(HelpButtonId);
        Find<UiButton>(ExitButtonId);
        Find<UiButton>(RandomButtonId);
        // Retail's pages start shown and the first SetProgressState hides all but one; hiding them first lets a page that composes a 3D character wait for its turn.
        foreach (var page in Enum.GetValues<CharGenPageId>())
        {
            Find<UiElement>(CharGenRules.PageElementId(page))?.SetVisible(false);
        }

        foreach (var page in Enum.GetValues<CharGenPageId>())
        {
            if (Find<UiElement>(CharGenRules.PageElementId(page)) is { } element)
            {
                _pages[page] = page switch
                {
                    CharGenPageId.Heritage => new CharGenHeritagePage(this, element),
                    CharGenPageId.Profession => new CharGenProfessionPage(this, element),
                    CharGenPageId.Skills => new CharGenSkillsPage(this, element),
                    CharGenPageId.Appearance => new CharGenAppearancePage(this, element),
                    CharGenPageId.Town => new CharGenTownPage(this, element),
                    _ => new CharGenSummaryPage(this, element),
                };
            }
        }

        SetProgressState(CharGenPageId.Heritage);
    }

    /// <summary>Raised when <see cref="PlaceholderNote"/> changes (a page opened).</summary>
    public event Action? PlaceholderNoteChanged;

    /// <summary>The element runtime (the pages' way to it).</summary>
    public UiElementManager ElementManager => Manager;

    /// <summary>The character being made (retail's <c>CharGenState</c> of the player system).</summary>
    public CharGenState State { get; }

    /// <summary>The CharGen data the character is made from.</summary>
    public CharGenCatalog Catalog => _setup.Catalog;

    /// <summary><c>CPlayerSystem::AccountHasThroneOfDestiny</c>: see <see cref="CharGenSetup.HasThroneOfDestiny"/>.</summary>
    public bool HasThroneOfDestiny => _setup.HasThroneOfDestiny;

    /// <summary>The page showing (<c>m_eProgressState</c>).</summary>
    public CharGenPageId Progress { get; private set; }

    /// <summary>The heritage page, or null when the layout lacks it.</summary>
    public CharGenHeritagePage? Heritage => PageOf<CharGenHeritagePage>(CharGenPageId.Heritage);

    /// <summary>The profession page, or null when the layout lacks it.</summary>
    public CharGenProfessionPage? Profession => PageOf<CharGenProfessionPage>(CharGenPageId.Profession);

    /// <summary>The skills page, or null when the layout lacks it.</summary>
    public CharGenSkillsPage? Skills => PageOf<CharGenSkillsPage>(CharGenPageId.Skills);

    /// <summary>The appearance page, or null when the layout lacks it.</summary>
    public CharGenAppearancePage? Appearance => PageOf<CharGenAppearancePage>(CharGenPageId.Appearance);

    /// <summary>The start town page, or null when the layout lacks it.</summary>
    public CharGenTownPage? Town => PageOf<CharGenTownPage>(CharGenPageId.Town);

    /// <summary>The summary page, or null when the layout lacks it.</summary>
    public CharGenSummaryPage? Summary => PageOf<CharGenSummaryPage>(CharGenPageId.Summary);

    /// <summary>What the 3D characters read from the dats, or null when the host gave none.</summary>
    public Cg3DViewData? View3DData => _setup.View3D;

    /// <summary>A 3D character view of the screen's character for a page (<c>new gmCG3DView</c>), or null when the host gave no view data.</summary>
    internal Cg3DViewModel? NewView3D() => _setup.View3D is { } data ? new Cg3DViewModel(State, data, Log) : null;

    /// <summary>Has the host draw <paramref name="view"/> in viewport element <paramref name="viewportElementId"/>; null when the host draws none.</summary>
    internal IDisposable? AttachView(uint viewportElementId, Cg3DViewModel view) => _setup.ViewHost?.Attach(viewportElementId, view);

    /// <summary>Page <paramref name="id"/>, or null when the layout lacks it.</summary>
    public CharGenPage? Page(CharGenPageId id) => _pages.GetValueOrDefault(id);

    /// <summary>The tab of <paramref name="id"/>.</summary>
    public UiButton? TabOf(CharGenPageId id) => _tabs.GetValueOrDefault(id);

    /// <summary>Back.</summary>
    public UiButton? LeftButton => _left;

    /// <summary>Next.</summary>
    public UiButton? RightButton => _right;

    /// <summary>Finish.</summary>
    public UiButton? FinishButton => _finish;

    /// <summary>The master page.</summary>
    public UiElement? MasterPage => _master;

    /// <summary>The context of the open exit warning, or 0.</summary>
    public uint ExitDialog => _exitContext;

    /// <summary>The context of the open randomize warning, or 0.</summary>
    public uint RandomizeDialog => _randomizeContext;

    /// <summary>The context of the open "Throne of Destiny required" message, or 0.</summary>
    public uint ThroneDialog => _throneContext;

    /// <summary>The context of the open "unspent credits" question, or 0.</summary>
    public uint CreditDialog => _creditContext;

    /// <summary>The context of the open error message of a refused create, or 0.</summary>
    public uint ErrorDialog => _errorContext;

    /// <summary>The context of the open "no name" message, or 0.</summary>
    public uint NoNameDialog => _noNameContext;

    /// <summary>True from the moment the create was sent until its answer (<c>CG_VERIFICATION_RESPONSE_PENDING</c>).</summary>
    public bool IsCreatePending => _verificationPending;

    /// <inheritdoc/>
    public string? PlaceholderNote => _note;

    // ---------------------------------------------------------------- Pages

    /// <summary>
    /// <c>gmCharGenMainUI::SetProgressState</c>: page <paramref name="requested"/> opens (an Olthoi character's request lands where
    /// <see cref="CharGenRules.Resolve"/> says), with the tabs, Next and Finish as the page and the heritage want them.
    /// </summary>
    public void SetProgressState(CharGenPageId requested)
    {
        if (Appearance is { Root.Visible: true } leaving)
        {
            leaving.PageLeft(); // before the next page composes the character: the headgear the face half holds goes back on it
        }

        foreach (var page in _pages.Values)
        {
            page.Root.SetVisible(false);
        }

        _right?.SetVisible(true);
        _finish?.SetVisible(false);
        var previous = Progress;
        Progress = requested;
        foreach (var tab in _tabs.Values)
        {
            tab.SetState(UiStateIds.Normal);
        }

        ShowOptionalTabs(!CharGenRules.IsOlthoi(State.Heritage));
        Progress = CharGenRules.Resolve(requested, previous, State.Heritage);
        _master?.SetState(CharGenRules.MasterState(Progress));
        if (_pages.TryGetValue(Progress, out var shown))
        {
            shown.Root.SetVisible(true);
            _tabs.GetValueOrDefault(Progress)?.SetState(UiStateIds.Highlight);
            shown.Update();
        }

        if (Progress == CharGenPageId.Summary)
        {
            _right?.SetVisible(false);
            _finish?.SetVisible(true);
        }

        _note = shown?.PlaceholderText;
        PlaceholderNoteChanged?.Invoke();
    }

    private void ShowOptionalTabs(bool visible)
    {
        foreach (var page in new[] { CharGenPageId.Profession, CharGenPageId.Skills, CharGenPageId.Town })
        {
            _tabs.GetValueOrDefault(page)?.SetVisible(visible);
        }
    }

    /// <summary>
    /// <c>gmCharGenMainUI::DoRandom</c>: the Random button draws the current page again at random: a heritage, a profession (with its default
    /// attributes), the skills, the appearance (the clothing when the clothes half shows), the start town, or (on the summary page, after a warning) the
    /// whole character.
    /// </summary>
    public void DoRandom()
    {
        switch (Progress)
        {
            case CharGenPageId.Heritage:
                State.RandomizeHeritageGroup(HasThroneOfDestiny);
                Heritage?.Update();
                break;
            case CharGenPageId.Profession:
                State.RandomizeTemplate();
                Profession?.UpdateToDefaultAttributes();
                break;
            case CharGenPageId.Skills:
                State.RandomizeSkills();
                Skills?.Update();
                break;
            case CharGenPageId.Appearance:
                Appearance?.Random();
                break;
            case CharGenPageId.Town:
                State.SetStartArea(CharGenDice.RandInt(_dice, HasThroneOfDestiny ? 4 : 3));
                Page(CharGenPageId.Town)?.Update();
                break;
            case CharGenPageId.Summary:
                State.RandomizeCharacter(HasThroneOfDestiny);
                Page(CharGenPageId.Summary)?.Update();
                break;
        }
    }

    // ---------------------------------------------------------------- Messages

    /// <inheritdoc/>
    protected override UiListenResult OnElementMessage(in UiElementMessage message)
    {
        if (message.MessageId != UiMessageIds.ButtonClick)
        {
            return UiListenResult.Default;
        }

        var id = message.ElementId;
        if (_tabs.Where(t => t.Value.ElementId == id).Select(t => (CharGenPageId?)t.Key).FirstOrDefault() is { } tabPage)
        {
            SetProgressState(tabPage);
            return UiListenResult.Default;
        }

        switch (id)
        {
            case LeftButtonId when Progress <= CharGenPageId.Heritage:
            case ExitButtonId:
                DoExit();
                break;
            case LeftButtonId:
                SetProgressState(Progress - 1);
                break;
            case RightButtonId when Progress < CharGenPageId.Summary:
                SetProgressState(Progress + 1);
                break;
            case RandomButtonId when Progress == CharGenPageId.Summary:
                MakeRandomizeWarningDialog();
                break;
            case RandomButtonId:
                DoRandom();
                break;
            case FinishButtonId when Progress == CharGenPageId.Summary:
                DoFinish(true);
                break;
            case HelpButtonId:
                MakeHelpDialog();
                break;
            default:
                if (CharGenRules.HeritageOfButton(id) is { } heritage)
                {
                    HeritageClicked(heritage);
                }

                break;
        }

        return UiListenResult.Default;
    }

    /// <summary>
    /// Retail's main screen shows or hides the optional tabs when a heritage button is clicked (<c>ListenToElementMessage</c>): shown for the ten
    /// buttons it lists, hidden for the two Olthoi ones. Lifestone shows them by the character's heritage after the page has heard the click, which
    /// differs only for the Lugian button (not in retail's list, so the tabs stay hidden after an Olthoi) and where the Throne of Destiny is missing.
    /// </summary>
    private void HeritageClicked(CharGenHeritageEntry clicked)
    {
        if ((Context.Manager.Services.RetailGlitches & UiRetailGlitches.CharGenHeritageTabs) != 0)
        {
            if (clicked.Heritage == 8)
            {
                return;
            }

            ShowOptionalTabs(!CharGenRules.IsOlthoi(clicked.Heritage));
            return;
        }

        ShowOptionalTabs(!CharGenRules.IsOlthoi(State.Heritage));
    }

    /// <inheritdoc/>
    public void ListenToGlobalMessage(uint messageId, int data)
    {
        if (IsDisposed || messageId != UiGlobalMessageIds.ActionStart)
        {
            return;
        }

        if (data == ExitAction)
        {
            DoExit();
        }
        else if (data == HelpAction)
        {
            MakeHelpDialog();
        }
    }

    // ---------------------------------------------------------------- Dialogs

    /// <summary><c>gmCharGenMainUI::DoExit</c>: "are you sure" (the exit warning); yes returns to character management. False when it is already asking.</summary>
    public bool DoExit()
    {
        if (_exitContext != 0)
        {
            return false;
        }

        var data = UiDialogData.Of(UiDialogType.Confirmation, Pregame("ID_CharGen_ExitWarning")).WithFlag(UiPropertyIds.DialogModal, true);
        _exitContext = Manager.Dialogs.MakeCallbackDialog(data, answer =>
        {
            _exitContext = 0;
            if (!IsDisposed && answer.ConfirmationResponse == true)
            {
                Context.Flow.QueueUIMode(UiModes.CharacterManagement);
            }
        });
        return _exitContext != 0;
    }

    /// <summary><c>gmCharGenMainUI::MakeRandomizeWarningDialog</c>: the summary page's Random asks before it replaces the whole character; yes does it.</summary>
    public bool MakeRandomizeWarningDialog()
    {
        if (_randomizeContext != 0)
        {
            return false;
        }

        var data = UiDialogData.Of(UiDialogType.Confirmation, Pregame("ID_CharGen_RandomizeWarning")).WithFlag(UiPropertyIds.DialogModal, true);
        _randomizeContext = Manager.Dialogs.MakeCallbackDialog(data, answer =>
        {
            _randomizeContext = 0;
            if (!IsDisposed && answer.ConfirmationResponse == true)
            {
                DoRandom();
            }
        });
        return _randomizeContext != 0;
    }

    /// <summary><c>gmCharGenMainUI::MakeToDWarningDialog</c>: the message that says the Viamontian heritage needs the Throne of Destiny. False when it is already showing.</summary>
    public bool MakeToDWarningDialog()
    {
        if (_throneContext != 0)
        {
            return false;
        }

        var data = UiDialogData.Of(UiDialogType.Message, Pregame("ID_CharGen_ToDRequiredWarning")).WithFlag(UiPropertyIds.DialogModal, true);
        _throneContext = Manager.Dialogs.MakeCallbackDialog(data, _ => _throneContext = 0);
        return _throneContext != 0;
    }

    // ---------------------------------------------------------------- Finish and the create

    /// <summary>
    /// <c>gmCharGenMainUI::DoFinish</c>: the name is trimmed of "[", "]" and spaces and kept. With no name typed (or an empty one) a message says so; with
    /// attribute credits left and <paramref name="creditCheck"/> a question asks first (its yes calls this again without the check); otherwise, when no
    /// create is waiting for its answer, the character is checked and sent. True when a create was sent.
    /// </summary>
    public bool DoFinish(bool creditCheck)
    {
        var name = State.Name.Trim('[', ']', ' ');
        State.SetName(name);
        if (Summary is { NameEntered: true } && name.Length != 0)
        {
            if (creditCheck && State.RemainingAttributeCredits > 0)
            {
                MakeCreditWarningDialog();
                return false;
            }

            if (!_verificationPending)
            {
                return SendCreate();
            }

            return false;
        }

        MakeNoNameWarningDialog();
        return false;
    }

    /// <summary>
    /// <c>Proto_UI::SendCharGenResult</c> by way of the action API: the character is verified (<see cref="CharGenVerifier"/>, as the server will), and
    /// sent as the create intent. A character that does not verify, or a request the session refuses, is said on screen in Lifestone's own words.
    /// </summary>
    private bool SendCreate()
    {
        Appearance?.PageLeft(); // a create sent while the face half still holds the headgear takes it along
        var result = State.GetCharGenResult();
        if (!CharGenVerifier.Verify(result, Catalog, out var problem, HasThroneOfDestiny))
        {
            Log.Error(UiElementManager.Category, $"character creation built a character that does not verify, so it was not sent: {problem}");
            MakeErrorMessageDialog(UiStringRef.FromPlainText($"Lifestone: this character cannot be created ({problem})."));
            return false;
        }

        _verificationPending = true;
        var handle = Session.Actions.CreateCharacter(result.ToIntent(), ActionSource.Ui);
        if (handle.IsCompleted)
        {
            OnCreateActionDone(handle);
        }
        else
        {
            handle.Completed += OnCreateActionDone;
        }

        return true;
    }

    /// <summary>
    /// The create's action ended. A server's answer (F643 or a character error the flow routes) is the event handler's and the flow's; any other ending
    /// (not sent, no answer in time) clears the wait and is said in Lifestone's words, as a placeholder when the session is offline.
    /// </summary>
    private void OnCreateActionDone(ActionHandle handle)
    {
        if (IsDisposed || handle.Outcome is not { } outcome || outcome.Succeeded || outcome.Kind == ActionOutcomeKind.ServerError)
        {
            return;
        }

        _verificationPending = false;
        var offline = Session.State.IsOffline && outcome.Kind == ActionOutcomeKind.Refused;
        var text = offline
            ? $"{NotInM2.Prefix}creating a character without a server (this is the offline stand-in session)"
            : $"Lifestone: the character was not created ({outcome.Kind}{(string.IsNullOrEmpty(outcome.Detail) ? string.Empty : ": " + outcome.Detail)})";
        Log.Warn(UiElementManager.Category, offline ? $"placeholder: {text}" : text);
        MakeErrorMessageDialog(UiStringRef.FromPlainText(text));
    }

    /// <summary>
    /// <c>gmCharGenMainUI::RecvNotice_CharGenVerificationResponse</c>: the server's answer to the create. Ok waits for the new character to show in the
    /// list; the name reserved (3), banned (4) and refused to the account (7) say so, and every other result says the name database is down.
    /// </summary>
    private void OnCreateResult(in CharacterCreateResult e)
    {
        if (e.Restore || !_verificationPending)
        {
            return;
        }

        _verificationPending = false;
        string? token;
        switch (e.ResultCode)
        {
            case 1:
                _awaitingCharacterSet = true;
                _createdGuid = e.Guid;
                return;
            case 3:
                token = "ID_Character_Err_NameReserved";
                break;
            case 4:
                token = "ID_Character_Err_NameBanned";
                break;
            case 7:
                token = "ID_Character_Err_NameAdminDenied";
                break;
            default:
                token = "ID_Character_Err_NameDBDown";
                break;
        }

        MakeErrorMessageDialog(Pregame(token));
    }

    /// <summary>
    /// <c>gmCharGenMainUI::Update</c>: once a create succeeded, the new character is looked up in the list and logged in at once
    /// (<c>CPlayerSystem::LogOnCharacter</c>, 11.4186 0x0055F890): the Enter action is sent for it, and character management is queued with the
    /// character selected, where the player's arrival (<c>RecvNotice_BeginEnterWorld</c>) starts the game screen as it does for the Enter button. An Enter
    /// that cannot go (the offline stand-in session, a refused or timed-out request) is said on that screen in Lifestone's words, as a placeholder when
    /// the session is offline. The "Entering World" dialog retail shows in character management is not shown for this entry
    /// (<c>TODO(retailui-chargen-enter-dialog)</c>: retail's own dialog for <c>LogOnCharacter</c> is not read yet).
    /// </summary>
    public override void Update()
    {
        if (!_awaitingCharacterSet || IsDisposed)
        {
            return;
        }

        _awaitingCharacterSet = false;
        var characters = Session.State.Characters;
        Context.Flow.QueueUIMode(UiModes.CharacterManagement); // first: an Enter that fails at once queues the same mode again with its reason
        if (_createdGuid != 0 && characters.IndexOf(_createdGuid) >= 0)
        {
            Session.Actions.SelectCharacter(_createdGuid, ActionSource.Ui);
            Manager.LogOnce(
                "chargen:enter-dialog",
                LogSeverity.Warning,
                "TODO(retailui-chargen-enter-dialog): the new character is logged in at once as retail does (CPlayerSystem::LogOnCharacter), but the Entering World dialog is not shown for it; retail's dialog for this path is not read yet");
            EnterCreatedCharacter(_createdGuid);
        }
        else
        {
            Log.Warn(UiElementManager.Category, "the created character is not in the list, so it was not logged in; character management shows the list");
        }
    }

    /// <summary>
    /// <c>CPlayerSystem::LogOnCharacter</c> by way of the action API (the same Enter action as character management's Enter button). A failure that is
    /// not a server's answer (those are the flow's) queues character management again with the reason: this screen is gone by then, so the flow says it.
    /// </summary>
    private void EnterCreatedCharacter(uint guid)
    {
        var session = Session;
        var flow = Context.Flow;
        var log = Log;
        var handle = session.Actions.EnterWorld(guid, ActionSource.Ui);

        void Done(ActionHandle h)
        {
            if (h.Outcome is not { } outcome || outcome.Succeeded || outcome.Kind == ActionOutcomeKind.ServerError)
            {
                return;
            }

            var offline = session.State.IsOffline && outcome.Kind == ActionOutcomeKind.Refused;
            var text = offline
                ? $"{NotInM2.Prefix}entering the world without a server (this is the offline stand-in session)"
                : $"Lifestone: entering the world did not go through ({outcome.Kind}{(string.IsNullOrEmpty(outcome.Detail) ? string.Empty : ": " + outcome.Detail)})";
            log.Warn(UiElementManager.Category, offline ? $"placeholder: {text}" : text);
            flow.QueueUIModeWithError(UiModes.CharacterManagement, UiStringRef.FromPlainText(text));
        }

        if (handle.IsCompleted)
        {
            Done(handle);
        }
        else
        {
            handle.Completed += Done;
        }
    }

    /// <summary><c>gmCharGenMainUI::MakeCreditWarningDialog</c>: the question that asks whether to finish with attribute credits unspent; yes finishes without the check.</summary>
    public bool MakeCreditWarningDialog()
    {
        if (_creditContext != 0)
        {
            return false;
        }

        var data = UiDialogData.Of(UiDialogType.Confirmation, Pregame("ID_CharGen_CreditWarning")).WithFlag(UiPropertyIds.DialogModal, true);
        _creditContext = Manager.Dialogs.MakeCallbackDialog(data, answer =>
        {
            _creditContext = 0;
            if (!IsDisposed && answer.ConfirmationResponse == true)
            {
                DoFinish(false);
            }
        });
        return _creditContext != 0;
    }

    /// <summary><c>gmCharGenMainUI::DoFinish</c>'s message for a character with no name typed (<c>ID_CharGen_NoNameWarning</c>).</summary>
    public bool MakeNoNameWarningDialog()
    {
        if (_noNameContext != 0)
        {
            return false;
        }

        var data = UiDialogData.Of(UiDialogType.Message, Pregame("ID_CharGen_NoNameWarning")).WithFlag(UiPropertyIds.DialogModal, true);
        _noNameContext = Manager.Dialogs.MakeCallbackDialog(data, _ => _noNameContext = 0);
        return _noNameContext != 0;
    }

    /// <summary><c>gmCharGenMainUI::MakeErrorMessageDialog</c>: a message with <paramref name="error"/>; one at a time.</summary>
    public bool MakeErrorMessageDialog(in UiStringRef error)
    {
        if (_errorContext != 0)
        {
            return false;
        }

        var data = UiDialogData.Of(UiDialogType.Message, error).WithFlag(UiPropertyIds.DialogModal, true);
        _errorContext = Manager.Dialogs.MakeCallbackDialog(data, _ => _errorContext = 0);
        return _errorContext != 0;
    }

    /// <summary>
    /// The Help button and key: retail opens its help (<c>KeyStone::OpenHelp</c>, from the global message for action 123; the button's own path is not in
    /// the decompile read), which M2 does not build, so a "Not in Lifestone M2" dialog says so (a placeholder, logged).
    /// </summary>
    private void MakeHelpDialog()
    {
        if (_helpContext != 0 && Manager.Dialogs.Find(_helpContext) is not null)
        {
            return;
        }

        Manager.LogOnce("chargen:help", LogSeverity.Warning, "TODO(retailui-chargen-help): the Help button and key open retail's help (KeyStone::OpenHelp), which M2 does not build; a 'not in M2' dialog stands in (placeholder)");
        _helpContext = NotInM2.Show(Manager.Dialogs, Log, "Help");
    }

    private T? PageOf<T>(CharGenPageId id)
        where T : CharGenPage => _pages.GetValueOrDefault(id) as T;

    /// <inheritdoc/>
    protected override void OnDisposing()
    {
        Manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.ActionStart);
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        foreach (var page in _pages.Values)
        {
            page.Dispose();
        }

        foreach (var context in new[] { _exitContext, _randomizeContext, _throneContext, _helpContext, _creditContext, _errorContext, _noNameContext })
        {
            Manager.Dialogs.CloseDialog(context);
        }

        _exitContext = _randomizeContext = _throneContext = _helpContext = _creditContext = _errorContext = _noNameContext = 0;
    }
}
