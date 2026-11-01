using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Options;

// 11.4186 addresses. Cited at 11.6096 in the comments below because this file was re-expressed against the 11.6096 symbol-table pin; the same
// functions in 11.4186 (the decompile symbols) are at: UIOption_Checkbox::Register 0x00486C40, ::Apply 0x00486C60; gmCharacterSettingsUI::InitOptions
// 0x004A02F0; PlayerOptionPage::AddHeader 0x004F28B0, ::AddToggleOption 0x004F2990, ::OnOptionChanged 0x004F27D0;
// gmGameplayOptionsUI::ListenToElementMessage 0x0049E110.
/// <summary>
/// An option check box (<c>UIOption_Checkbox</c>, class 0x10000035, <c>Register</c> 11.6096 0x00487000): a button whose label is the option's string and whose checked state is the button's
/// highlight (retail reads and writes attribute 0x0E, which is <see cref="UiPropertyIds.ButtonHighlighted"/>). It keeps the option's saved and current values (<c>m_saved</c>, <c>m_current</c>): a click changes
/// the current value; Apply saves it, Reset restores the saved one and Defaults the default.
/// </summary>
public sealed class UiOptionCheckbox : UiButton
{
    /// <summary>The check box of an element description.</summary>
    public UiOptionCheckbox(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The option the box shows (null for a box that belongs to no option).</summary>
    public PlayerOptionDef? Option { get; internal set; }

    /// <summary>The value last applied (<c>m_saved</c>).</summary>
    public bool Saved { get; internal set; }

    /// <summary>The value shown (<c>m_current</c>): the button's highlight.</summary>
    public bool Current
    {
        get => Highlighted;
        internal set
        {
            if (Highlighted != value)
            {
                SetBoolProperty(UiPropertyIds.ButtonHighlighted, value);
            }
        }
    }

    /// <summary><c>UIOption_Checkbox::Changed</c> (11.6096 0x00487020 is Apply; Changed is <c>m_saved != m_current</c>).</summary>
    public bool Changed => Saved != Current;

    /// <summary>True when the layout makes the button toggle by itself (property 0x0B); otherwise the page flips it on a click.</summary>
    internal bool TogglesItself => InqProperty(UiPropertyIds.ButtonBooleanButton, out _);
}

/// <summary>
/// The options panel (docs/retail-ui.md section 21.8): page 10 of the panel host (0x1000018D, a tabbed <c>UiPanel</c>) with its four tab pages. What Lifestone M2 builds:
/// <list type="bullet">
/// <item><b>Character</b> (class 0x10000027, <c>gmCharacterSettingsUI</c>, listbox 0x100001FA): the headers, separators and check boxes <c>InitOptions</c> (11.6096 0x004A05B0) adds through
/// <c>PlayerOptionPage::AddHeader</c> (0x004F3510) and <c>AddToggleOption</c> (0x004F35F0) from the list templates 0, 1 and 2, with the buttons Apply (0x100001FC), Reset (0x100001FD) and Defaults
/// (0x100001FE). A click changes the box only; <c>PlayerOptionPage::OnOptionChanged</c> (0x004F3430) enables Apply and Reset while something differs and ghosts them otherwise. Apply writes the changed
/// options into <see cref="GameOptions"/> and so to the server (retail's <c>UIOption_Checkbox::Apply</c>, 0x00487020, wrote <c>PlayerModule::SetOption</c>; M5 6 u1, D32: all 49 boxes are settable,
/// the words go out in one packet, and ACE returns them with the next login's description). The few options with a client-side effect are listed in <see cref="PlayerOptionTable"/>; changing any other
/// logs <c>TODO(g6-option-effects)</c> once, and the panel claims no effect for it.</item>
/// <item><b>Gameplay Options</b> (class 0x10000029, <c>gmGameplayOptionsUI::ListenToElementMessage</c> 0x0049E390): "Exit to Character Selection" (0x10000203) is the logout (<see cref="GameLogout"/>); the
/// others answer a click with the "not in M2" message (<c>TODO(g6-options-buttons)</c>: keyboard configuration, mouse turning, help files, urgent assistance and abuse reports open web pages or windows Lifestone has not built; Exit Game
/// asks the same confirmation as the logout and then queues the epilogue, which quits the session and closes the application).</item>
/// <item><b>Chat</b> (class 0x10000042) and <b>Config</b> (class 0x10000028): the pages are in the host but hold no rows (<c>TODO(g6-chat-options)</c>: the per-window chat settings; <c>TODO(g6-config-options)</c>:
/// sound, camera, graphics and input preferences belong to the client's own settings, not to the character).</item>
/// </list>
/// </summary>
public sealed class GameOptionsPanel : IUiElementListener, IDisposable
{
    /// <summary>The panel id of the options page.</summary>
    public const uint PanelId = 10;

    /// <summary>The string table enum (0x10000003) of the option strings.</summary>
    public const uint OptionTableEnum = 0x10000003;

    private const uint CharacterPageId = 0x10000211;
    private const uint ListId = 0x100001FA;
    private const uint ApplyId = 0x100001FC;
    private const uint ResetId = 0x100001FD;
    private const uint DefaultsId = 0x100001FE;
    private const uint GameplayPageId = 0x10000212;
    private const uint ExitToCharacterSelectionId = 0x10000203;
    private const uint ExitGameId = 0x10000617;
    private static readonly uint[] UnbuiltButtons = [0x10000204, 0x100005CC, 0x10000205, 0x10000206, 0x10000207];

    private readonly UiLoginContext _context;
    private readonly GamePanelHost _host;
    private readonly GameOptions _options;
    private readonly GameLogout _logout;
    private readonly List<UiOptionCheckbox> _boxes = [];
    private readonly IDisposable _subscription;
    private readonly Dictionary<uint, string> _unbuiltActions = [];
    private readonly Dictionary<uint, string> _unbuiltButtons = [];
    private UiElement? _gameplayPage;
    private UiListBox? _list;
    private UiButton? _apply;
    private UiButton? _reset;

    /// <summary>The options panel in <paramref name="host"/>; registers itself as the handler of page 10.</summary>
    public GameOptionsPanel(UiLoginContext context, GamePanelHost host, GameOptions options, GameLogout logout)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logout);
        _context = context;
        _host = host;
        _options = options;
        _logout = logout;
        host.SetPageHandler(PanelId, Open);
        _subscription = context.Session.Events.Subscribe<Lifestone.Client.Session.Contracts.QualitiesChanged>(OnQualitiesChanged);
        _options.Changed += OnOptionChanged;
        if (host.ElementFor(PanelId) is { } page)
        {
            BuildCharacterPage(page);
            BuildGameplayPage(page);
            Refresh();
        }
    }

    /// <summary>The options page (0x1000018D, a tabbed panel).</summary>
    public UiElement? Page => _host.ElementFor(PanelId);

    /// <summary>The check boxes of the character page, in page order.</summary>
    public IReadOnlyList<UiOptionCheckbox> Boxes => _boxes;

    /// <summary>The box of <paramref name="option"/>, or null.</summary>
    public UiOptionCheckbox? BoxOf(PlayerOptionDef option) => _boxes.FirstOrDefault(b => ReferenceEquals(b.Option, option));

    /// <summary>Scrolls the character page's list so <paramref name="option"/>'s row is at the top (<c>UIElement_ListBox::ScrollToShow</c>); false when it is not listed.</summary>
    public bool ScrollTo(PlayerOptionDef option)
    {
        var box = BoxOf(option);
        if (_list is null || box is null)
        {
            return false;
        }

        for (var i = 0; i < _list.Items.Count; i++)
        {
            for (var up = (UiElement?)box; up is not null; up = up.Parent)
            {
                if (ReferenceEquals(up, _list.Items[i]))
                {
                    _list.ScrollToShow(i);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The Apply button (0x100001FC).</summary>
    public UiButton? Apply => _apply;

    /// <summary>The Reset button (0x100001FD).</summary>
    public UiButton? Reset => _reset;

    /// <summary>Shows the page (the handler the host calls).</summary>
    public void Open()
    {
        Refresh();
        _host.SetPanelVisibility(PanelId, true);
    }

    private void OnQualitiesChanged(in Lifestone.Client.Session.Contracts.QualitiesChanged change)
    {
        if (change.Change == Lifestone.Client.Session.Contracts.QualityChange.Description)
        {
            Refresh(); // the description brings the character's options (RecvNotice_PlayerDescReceived)
        }
    }

    // An option changed somewhere else (the combat window's boxes, a refused send): the boxes the player is not editing follow it.
    private void OnOptionChanged(PlayerOptionDef option)
    {
        foreach (var box in _boxes)
        {
            if (box.Option is { } def && !box.Changed)
            {
                var value = _options.Get(def);
                box.Saved = value;
                box.Current = value;
            }
        }

        UpdateButtons();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _options.Changed -= OnOptionChanged;
        _subscription.Dispose();
    }

    /// <summary>Takes the boxes' values from the options (<c>RecvNotice_RefreshOptionsPanel</c>) and sets the buttons.</summary>
    public void Refresh()
    {
        foreach (var box in _boxes)
        {
            if (box.Option is { } option)
            {
                var value = _options.Get(option);
                box.Saved = value;
                box.Current = value;
            }
        }

        UpdateButtons();
    }

    /// <summary>Retail's Apply: sends the changed boxes' options to the server (<see cref="GameOptions.Apply"/>); a refused send leaves the boxes as they were shown.</summary>
    public void ApplyChanges()
    {
        var changed = _boxes.Where(static b => b.Option is not null && b.Changed).ToList();
        if (changed.Count > 0 && _options.Apply([.. changed.Select(static b => (b.Option!, b.Current))]))
        {
            foreach (var box in changed)
            {
                box.Saved = box.Current;
            }
        }

        UpdateButtons();
    }

    /// <summary>Retail's Reset: every box back to its saved value.</summary>
    public void ResetChanges()
    {
        foreach (var box in _boxes)
        {
            box.Current = box.Saved;
        }

        UpdateButtons();
    }

    /// <summary>Retail's Defaults (<c>UIOption_Checkbox::RestoreDefaultValue</c>, 0x00486CF0): every box to the option's default; Apply then sends them.</summary>
    public void RestoreDefaults()
    {
        foreach (var box in _boxes)
        {
            if (box.Option is { } option)
            {
                box.Current = option.Default;
            }
        }

        UpdateButtons();
    }

    /// <inheritdoc/>
    public UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.VisibilityToggleAction && ReferenceEquals(message.Element, _gameplayPage) && _unbuiltActions.TryGetValue(message.Param1, out var feature))
        {
            NotInM2.Show(_context.Manager.Dialogs, _context.Manager.Services.Log, feature);
            return UiListenResult.Handled;
        }

        if (message.MessageId != UiMessageIds.ButtonClick)
        {
            return UiListenResult.Default;
        }

        switch (message.Element)
        {
            case UiOptionCheckbox box:
                if (!box.TogglesItself)
                {
                    box.Current = !box.Current;
                }

                UpdateButtons();
                return UiListenResult.Handled;
            case { ElementId: ApplyId }:
                ApplyChanges();
                return UiListenResult.Handled;
            case { ElementId: ResetId }:
                ResetChanges();
                return UiListenResult.Handled;
            case { ElementId: DefaultsId }:
                RestoreDefaults();
                return UiListenResult.Handled;
            case { ElementId: ExitToCharacterSelectionId }:
                _logout.RequestEndCharacterSession();
                return UiListenResult.Handled;
            case UiButton { ElementId: var id } when _unbuiltButtons.TryGetValue(id, out var label):
                NotInM2.Show(_context.Manager.Dialogs, _context.Manager.Services.Log, label);
                return UiListenResult.Handled;
            case { ElementId: ExitGameId }:
                _logout.RequestExitGame();
                return UiListenResult.Handled;
            default:
                return UiListenResult.Default;
        }
    }

    private void UpdateButtons()
    {
        var changed = _boxes.Any(static b => b.Changed);
        _apply?.SetState(changed ? UiStateIds.Normal : UiStateIds.Ghosted);
        _reset?.SetState(changed ? UiStateIds.Normal : UiStateIds.Ghosted);
    }

    private void BuildCharacterPage(UiElement options)
    {
        var log = _context.Manager.Services.Log;
        var strings = _context.Manager.Services.Strings;
        var page = options.GetChildRecursive(CharacterPageId);
        if (page?.GetChildRecursive(ListId) is not UiListBox list || strings is null)
        {
            log.Error("retailui", "gmCharacterSettingsUI: the character options page or its list is missing; the options panel has no character options (TODO(g6-options-page))");
            return;
        }

        _list = list;
        _apply = page.GetChildRecursive(ApplyId) as UiButton;
        _reset = page.GetChildRecursive(ResetId) as UiButton;
        var defaults = page.GetChildRecursive(DefaultsId);
        foreach (var button in new UiElement?[] { _apply, _reset, defaults })
        {
            button?.RegisterForElementMessages(this);
        }

        foreach (var section in PlayerOptionTable.Sections)
        {
            var header = list.AddItemFromTemplateList(0); // PlayerOptionPage::AddHeader
            if (header is UiTextElement text && strings.TryReference(OptionTableEnum, section.HeaderToken, out var title))
            {
                text.SetStringInfo(title);
            }

            foreach (var option in section.Options)
            {
                var row = list.AddItemFromTemplateList(2); // PlayerOptionPage::AddToggleOption
                if (row?.GetChildRecursive(0x10000219) is not UiOptionCheckbox box)
                {
                    log.Error("retailui", $"PlayerOptionPage::AddToggleOption: the toggle row of {option.Name} has no check box (class 0x10000035 is not registered); the option is not listed (TODO(g6-options-page))");
                    continue;
                }

                box.Option = option;
                if (strings.TryReference(OptionTableEnum, option.LabelToken, out var label))
                {
                    box.SetStringInfo(label);
                }

                box.RegisterForElementMessages(this);
                _boxes.Add(box);
            }

            list.AddItemFromTemplateList(1); // PlayerOptionPage::AddSeperator
        }
    }

    private void BuildGameplayPage(UiElement options)
    {
        var page = options.GetChildRecursive(GameplayPageId);
        if (page is null)
        {
            return;
        }

        foreach (var id in new[] { ExitToCharacterSelectionId, ExitGameId })
        {
            page.GetChildRecursive(id)?.RegisterForElementMessages(this);
        }

        // The layout's ghosted art is its normal art, so a ghosted button would look live and do nothing. They stay in the normal state and a click answers with the "not in M2" message
        // (the toolbar's way), under the button's own label.
        // Each button's click is an input action (property 0x12) that the button fires itself, so the click is heard as that action's start (RegisterElementForInputAction) at the page.
        var unbuilt = 0;
        foreach (var id in UnbuiltButtons)
        {
            if (page.GetChildRecursive(id) is not UiButton button)
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(button.Text) ? "This options button" : button.Text.Trim();
            unbuilt++;
            if (button.TryGetEnumProperty(UiPropertyIds.ButtonClickAction, out var action) && action != 1)
            {
                _unbuiltActions[action] = label;
                _context.Manager.RegisterElementForInputAction(page, action);
            }
            else
            {
                _unbuiltButtons[id] = label; // a plain button: its click is heard as ButtonClick
                button.RegisterForElementMessages(this);
            }
        }

        _gameplayPage = page;
        page.RegisterForElementMessages(this);

        _context.Manager.Services.Log.Warn("retailui", $"placeholder: {unbuilt} buttons of the options panel's first tab (keyboard configuration, mouse turning, help files, urgent assistance, abuse report) answer a click with the 'not in M2' message; TODO(g6-options-buttons)");
    }
}
